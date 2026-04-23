using System.Collections.Concurrent;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Polly;
using Polly.Retry;
using Serilog;

namespace dvmig.Core
{
    /// <summary>
    /// Core engine responsible for synchronizing entities from the source to the target 
    /// Dataverse environment. Handles dependency resolution, retries, and data transformations.
    /// </summary>
    public partial class SyncEngine : ISyncEngine
    {
        private readonly IDataverseProvider _source;
        private readonly IDataverseProvider _target;
        private readonly IUserMapper _userMapper;
        private readonly IDataPreservationManager _dataPreservation;
        private readonly ILogger _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        private readonly ConcurrentDictionary<string, int> _recursionTracker =
            new ConcurrentDictionary<string, int>();

        private readonly ConcurrentDictionary<string, HashSet<string>>
            _triedDependencies =
                new ConcurrentDictionary<string, HashSet<string>>();

        private readonly ConcurrentDictionary<string, Guid> _idMappingCache =
            new ConcurrentDictionary<string, Guid>();

        private readonly ConcurrentDictionary<string, EntityMetadata>
            _metadataCache = new ConcurrentDictionary<string, EntityMetadata>();

        private const int MaxRecursionDepth = 3;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncEngine"/> class.
        /// </summary>
        /// <param name="source">The source Dataverse provider.</param>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="userMapper">The user mapping service.</param>
        /// <param name="dataPreservation">The data preservation manager.</param>
        /// <param name="logger">The logger instance.</param>
        public SyncEngine(
            IDataverseProvider source,
            IDataverseProvider target,
            IUserMapper userMapper,
            IDataPreservationManager dataPreservation,
            ILogger logger
        )
        {
            _source = source;
            _target = target;
            _userMapper = userMapper;
            _dataPreservation = dataPreservation;
            _logger = logger;

            _retryPolicy = Policy
                .Handle<Exception>(IsTransientError)
                .WaitAndRetryAsync(
                    5,
                    GetRetryDelay,
                    (ex, time, count, ctx) =>
                    {
                        _logger.Warning(
                            ex,
                            "Throttling or transient error. " +
                            "Retry {Count} in {Time}ms",
                            count,
                            time.TotalMilliseconds
                        );

                        return Task.CompletedTask;
                    }
                );
        }

        /// <summary>
        /// Determines if an exception represents a transient error that 
        /// should trigger a retry attempt (e.g., throttling, timeout).
        /// </summary>
        /// <param name="ex">The exception to evaluate.</param>
        /// <returns>True if the error is transient; otherwise, false.</returns>
        private bool IsTransientError(Exception ex)
        {
            var msg = ex.Message.ToLower();

            if (msg.Contains("8004410d") || msg.Contains("too many requests"))
            {
                return true;
            }

            return msg.Contains("generic sql error") ||
                   msg.Contains("timeout");
        }

        /// <summary>
        /// Calculates the delay before the next retry attempt, applying 
        /// exponential backoff. Caps the delay for Dataverse Service 
        /// Protection Limits (8004410d).
        /// </summary>
        /// <param name="retryCount">The current retry attempt number.</param>
        /// <param name="ex">The exception that triggered the retry.</param>
        /// <param name="ctx">The Polly execution context.</param>
        /// <returns>The duration to wait before retrying.</returns>
        private TimeSpan GetRetryDelay(
            int retryCount,
            Exception ex,
            Context ctx
        )
        {
            if (ex.Message.Contains("8004410d"))
            {
                _logger.Information(
                    "Service Protection Limit reached. " +
                    "Applying throttled backoff."
                );

                return TimeSpan.FromSeconds(
                    Math.Min(Math.Pow(2, retryCount), 30)
                );
            }

            return TimeSpan.FromSeconds(Math.Pow(2, retryCount));
        }

        /// <inheritdoc />
        public async Task SyncAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            IProgress<string>? progress = null,
            IProgress<bool>? recordProgress = null,
            CancellationToken ct = default
        )
        {
            _logger.Information(
                "Starting sync of {Count} entities",
                entities.Count()
            );

            _recursionTracker.Clear();

            progress?.Report(
                $"Starting migration of {entities.Count()} records..."
            );

            foreach (var entity in entities)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var success = await SyncRecordAsync(
                        entity,
                        options,
                        progress,
                        ct
                    );
                    recordProgress?.Report(success);
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Error syncing {Entity}:{Id}",
                        entity.LogicalName,
                        entity.Id
                    );

                    recordProgress?.Report(false);
                }
            }

            _logger.Information("Sync completed");
            progress?.Report("Migration completed successfully.");
        }

        /// <inheritdoc />
        public async Task<bool> SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            var recordKey = $"{entity.LogicalName}:{entity.Id}";
            var depth = _recursionTracker.AddOrUpdate(
                recordKey,
                1,
                (_, v) => v + 1
            );

            if (depth > MaxRecursionDepth)
            {
                _logger.Error(
                    "Max recursion depth reached for {Key}. Skipping.",
                    recordKey
                );

                _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);

                return false;
            }

            try
            {
                var metadata = await GetMetadataAsync(entity.LogicalName, ct);

                if (metadata?.IsIntersect == true)
                {
                    return await SyncIntersectEntityAsync(entity, options, ct);
                }

                var prepared = await PrepareEntityForTargetAsync(
                    entity,
                    metadata,
                    options,
                    ct
                );

                if (options.PreserveDates)
                {
                    try
                    {
                        await _dataPreservation.PreserveDatesAsync(entity, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(
                            ex,
                            "Date preservation failed for {Entity}:{Id}. " +
                            "Continuing sync.",
                            entity.LogicalName,
                            entity.Id
                        );

                        progress?.Report(
                            "Date preservation failed. Continuing..."
                        );
                    }
                }

                var success = await CreateWithFixStrategyAsync(
                    prepared,
                    options,
                    ct
                );

                if (success)
                {
                    _idMappingCache[recordKey] = entity.Id;
                    progress?.Report(
                        $"Synced {entity.LogicalName}:{entity.Id}"
                    );

                    if (options.PreserveDates)
                    {
                        await _dataPreservation.DeleteSourceDateAsync(
                            entity.LogicalName,
                            entity.Id,
                            ct
                        );
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to sync {Key}", recordKey);

                return false;
            }
            finally
            {
                _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);
            }
        }

        private async Task<bool> SyncIntersectEntityAsync(
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            try
            {
                var request = CreateAssociateRequest(entity);
                if (request == null)
                {
                    return false;
                }

                await _retryPolicy.ExecuteAsync(
                    () => _target.ExecuteAsync(request, ct)
                );

                _logger.Information(
                    "Associated N:N relationship {Key}",
                    entity.LogicalName
                );

                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already exists"))
                {
                    return true;
                }

                return await HandleSyncExceptionAsync(ex, entity, options, ct);
            }
        }

        private AssociateRequest? CreateAssociateRequest(Entity entity)
        {
            var references = entity.Attributes
                .Values.OfType<EntityReference>().ToList();

            if (references.Count < 2)
            {
                _logger.Warning(
                    "Intersect entity {Key} does not have " +
                    "two EntityReferences.",
                    entity.LogicalName
                );

                return null;
            }

            var request = new AssociateRequest
            {
                Target = references[0],
                Relationship = new Relationship(entity.LogicalName),
                RelatedEntities = new EntityReferenceCollection
                {
                    references[1]
                }
            };

            return request;
        }

        private async Task<bool> CreateWithFixStrategyAsync(
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(
                    () => _target.CreateAsync(entity, ct)
                );

                _logger.Information(
                    "Created {Key}:{Id}",
                    entity.LogicalName,
                    entity.Id
                );

                return true;
            }
            catch (Exception ex)
            {
                return await HandleSyncExceptionAsync(ex, entity, options, ct);
            }
        }
    }
}
