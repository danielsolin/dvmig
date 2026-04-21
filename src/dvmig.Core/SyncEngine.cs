using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Polly;
using Polly.Retry;
using Serilog;
using System.Collections.Concurrent;

namespace dvmig.Core
{
    public class SyncEngine : ISyncEngine
    {
        private readonly IDataverseProvider _source;
        private readonly IDataverseProvider _target;
        private readonly IUserMapper _userMapper;
        private readonly IDataPreservationManager _dataPreservation;
        private readonly ILogger _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        private readonly ConcurrentDictionary<string, int> _recursionTracker =
            new ConcurrentDictionary<string, int>();

        private readonly ConcurrentDictionary<string, EntityMetadata>
            _metadataCache = new ConcurrentDictionary<string, EntityMetadata>();

        private const int MaxRecursionDepth = 3;

        public SyncEngine(
            IDataverseProvider source,
            IDataverseProvider target,
            IUserMapper userMapper,
            IDataPreservationManager dataPreservation,
            ILogger logger)
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
                        _logger.Warning(ex, "Throttling or transient error. " +
                            "Retry {Count} in {Time}ms",
                            count, time.TotalMilliseconds);

                        return Task.CompletedTask;
                    });
        }

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

        private TimeSpan GetRetryDelay(int retryCount, Exception ex, Context ctx)
        {
            if (ex.Message.Contains("8004410d"))
            {
                _logger.Information("Service Protection Limit reached. " +
                    "Applying throttled backoff.");

                return TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryCount), 30));
            }

            return TimeSpan.FromSeconds(Math.Pow(2, retryCount));
        }

        public async Task SyncAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            _logger.Information("Starting sync of {Count} entities",
                entities.Count());
            _recursionTracker.Clear();

            progress?.Report($"Starting migration of {entities.Count()} records...");

            if (options.UseBulk)
            {
                await SyncBulkAsync(entities, options, progress, ct);
            }
            else
            {
                var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);
                var tasks = entities.Select(async entity =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        await SyncRecordAsync(entity, options, ct);
                        progress?.Report($"Synced {entity.LogicalName}:{entity.Id}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                await Task.WhenAll(tasks);
            }

            _logger.Information("Sync completed");
            progress?.Report("Migration completed successfully.");
        }

        public async Task SyncBulkAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var batches = entities
                .Select((e, i) => new { Entity = e, Index = i })
                .GroupBy(x => x.Index / options.BulkBatchSize)
                .Select(g => g.Select(x => x.Entity).ToList());

            foreach (var batch in batches)
            {
                _logger.Information("Processing bulk batch of {Count} records",
                    batch.Count);

                progress?.Report($"Processing batch of {batch.Count} records...");

                if (options.PreserveDates)
                {
                    await _dataPreservation.PreserveDatesBulkAsync(batch, ct);
                }

                var request = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    },
                    Requests = new OrganizationRequestCollection()
                };

                foreach (var entity in batch)
                {
                    var metadata = await GetMetadataAsync(entity.LogicalName, ct);

                    if (metadata?.IsIntersect == true)
                    {
                        request.Requests.Add(CreateAssociateRequest(entity));
                    }
                    else
                    {
                        var prepared = await PrepareEntityForTargetAsync(
                            entity,
                            metadata,
                            options,
                            ct);
                        request.Requests.Add(new CreateRequest { Target = prepared });
                    }
                }

                var response = (ExecuteMultipleResponse)await _retryPolicy
                    .ExecuteAsync(() => _target.ExecuteAsync(request, ct));

                if (response.Responses != null)
                {
                    foreach (var item in response.Responses)
                    {
                        if (item.Fault != null)
                        {
                            var failedEntity = batch[item.RequestIndex];
                            _logger.Warning("Bulk item {Index} failed: {Error}. " +
                                "Shunting to single sync.",
                                item.RequestIndex, item.Fault.Message);

                            progress?.Report($"Shunting failed record {failedEntity.LogicalName} to retry...");
                            await SyncRecordAsync(failedEntity, options, ct);
                        }
                    }
                }
            }
        }

        public async Task<bool> SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            CancellationToken ct = default)
        {
            var recordKey = $"{entity.LogicalName}:{entity.Id}";
            var depth = _recursionTracker.AddOrUpdate(recordKey, 1, (_, v) => v + 1);

            if (depth > MaxRecursionDepth)
            {
                _logger.Error("Max recursion depth reached for {Key}. Skipping.",
                    recordKey);

                return false;
            }

            try
            {
                var metadata = await GetMetadataAsync(entity.LogicalName, ct);

                if (metadata?.IsIntersect == true)
                {
                    return await SyncIntersectEntityAsync(entity, options, ct);
                }

                if (options.SkipExisting)
                {
                    var existing = await _target.RetrieveAsync(
                        entity.LogicalName,
                        entity.Id,
                        new[] { "modifiedon" },
                        ct);

                    if (existing != null)
                    {
                        return true;
                    }
                }

                var prepared = await PrepareEntityForTargetAsync(
                    entity,
                    metadata,
                    options,
                    ct);

                if (options.PreserveDates)
                {
                    await _dataPreservation.PreserveDatesAsync(entity, ct);
                }

                return await CreateWithFixStrategyAsync(prepared, options, ct);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to sync {Key}", recordKey);

                return false;
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

                await _retryPolicy.ExecuteAsync(() => _target.ExecuteAsync(request, ct));
                _logger.Information("Associated N:N relationship {Key}",
                    entity.LogicalName);

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
                _logger.Warning("Intersect entity {Key} does not have " +
                    "two EntityReferences.", entity.LogicalName);

                return null;
            }

            var request = new AssociateRequest
            {
                Target = references[0],
                Relationship = new Relationship(entity.LogicalName),
                RelatedEntities = new EntityReferenceCollection { references[1] }
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
                await _retryPolicy.ExecuteAsync(() => _target.CreateAsync(entity, ct));
                _logger.Information("Created {Key}:{Id}",
                    entity.LogicalName, entity.Id);

                return true;
            }
            catch (Exception ex)
            {
                return await HandleSyncExceptionAsync(ex, entity, options, ct);
            }
        }

        private async Task<bool> HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            var msg = ex.Message.ToLower();

            if (msg.Contains("does not exist"))
            {
                return await ResolveMissingDependencyAsync(ex, entity, options, ct);
            }

            if (msg.Contains("cannot be modified") ||
                msg.Contains("cannot be set on creation") ||
                msg.Contains("outside the valid range"))
            {
                return await StripAttributeAndRetryAsync(ex, entity, options, ct);
            }

            _logger.Error(ex, "Unresolved error for {Key}:{Id}",
                entity.LogicalName, entity.Id);

            return false;
        }

        private async Task<bool> StripAttributeAndRetryAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            var match = System.Text.RegularExpressions.Regex.Match(ex.Message,
                @"'(\w+)'");

            if (match.Success)
            {
                var attrName = match.Groups[1].Value;
                if (entity.Attributes.Contains(attrName))
                {
                    _logger.Warning("Stripping attribute '{Attr}' for {Key}:{Id}",
                        attrName, entity.LogicalName, entity.Id);

                    entity.Attributes.Remove(attrName);

                    return await CreateWithFixStrategyAsync(entity, options, ct);
                }
            }

            return false;
        }

        private async Task<bool> ResolveMissingDependencyAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            var match = System.Text.RegularExpressions.Regex.Match(ex.Message,
                @"(\w+) with Id=([a-fA-F0-9-]+) does not exist");

            if (match.Success)
            {
                var missingType = match.Groups[1].Value;
                var missingId = Guid.Parse(match.Groups[2].Value);

                var missingRecord = await _source.RetrieveAsync(
                    missingType, missingId, null, ct);

                if (missingRecord != null)
                {
                    var success = await SyncRecordAsync(missingRecord, options, ct);
                    if (success)
                    {
                        var metadata = await GetMetadataAsync(entity.LogicalName, ct);
                        if (metadata?.IsIntersect == true)
                        {
                            return await SyncIntersectEntityAsync(entity, options, ct);
                        }

                        return await CreateWithFixStrategyAsync(entity, options, ct);
                    }
                }
            }

            return false;
        }

        private async Task<Entity> PrepareEntityForTargetAsync(
            Entity entity,
            EntityMetadata? metadata,
            SyncOptions options,
            CancellationToken ct)
        {
            var target = new Entity(entity.LogicalName, entity.Id);

            foreach (var attr in entity.Attributes)
            {
                if (IsForbiddenAttribute(attr.Key))
                {
                    continue;
                }

                if (IsUserAttribute(attr.Key) && attr.Value is EntityReference userRef)
                {
                    var mapped = await _userMapper.MapUserAsync(userRef, ct);
                    if (mapped != null)
                    {
                        target[attr.Key] = mapped;

                        continue;
                    }
                }

                if (metadata?.Attributes != null)
                {
                    var attrMeta = metadata.Attributes
                        .FirstOrDefault(a => a.LogicalName == attr.Key);

                    if (attrMeta != null && attrMeta.IsValidForCreate == false)
                    {
                        _logger.Debug("Proactively stripping {Attr} for {Entity}",
                            attr.Key, entity.LogicalName);

                        continue;
                    }
                }

                target[attr.Key] = attr.Value;
            }

            return target;
        }

        private async Task<EntityMetadata?> GetMetadataAsync(
            string logicalName,
            CancellationToken ct)
        {
            if (_metadataCache.TryGetValue(logicalName, out var meta))
            {
                return meta;
            }

            try
            {
                var newMeta = await _target.GetEntityMetadataAsync(logicalName, ct);
                if (newMeta != null)
                {
                    _metadataCache[logicalName] = newMeta;
                }

                return newMeta;
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not fetch metadata for {Entity}: {Msg}",
                    logicalName, ex.Message);

                return null;
            }
        }

        private bool IsForbiddenAttribute(string attrName)
        {
            var forbidden = new[] {
                "createdon", "modifiedon", "versionnumber"
            };

            return forbidden.Contains(attrName.ToLower());
        }

        private bool IsUserAttribute(string attrName)
        {
            var userFields = new[] { "ownerid", "createdby", "modifiedby" };

            return userFields.Contains(attrName.ToLower());
        }
    }
}
