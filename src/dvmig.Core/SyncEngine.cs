using dvmig.Providers;
using Microsoft.Xrm.Sdk;
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
        private readonly ILogger _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        // Cache to track processed records to prevent infinite recursion
        private readonly ConcurrentDictionary<string, int> _recursionTracker =
            new ConcurrentDictionary<string, int>();

        private const int MaxRecursionDepth = 3;

        public SyncEngine(IDataverseProvider source, IDataverseProvider target, ILogger logger)
        {
            _source = source;
            _target = target;
            _logger = logger;

            // Basic retry for transient Dataverse errors
            _retryPolicy = Policy
                .Handle<Exception>(ex => ex.Message.Contains("Generic SQL error") ||
                                         ex.Message.Contains("Timeout"))
                .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)),
                    (ex, time, count, ctx) =>
                    {
                        _logger.Warning(ex, "Transient error. Retry {Count} in {Time}s",
                            count, time.TotalSeconds);
                    });
        }

        public async Task SyncAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            CancellationToken ct = default)
        {
            _logger.Information("Starting sync of {Count} entities", entities.Count());
            _recursionTracker.Clear();

            var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);
            var tasks = entities.Select(async entity =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await SyncRecordAsync(entity, options, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            _logger.Information("Sync completed");
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

            _logger.Debug("Syncing {Key} (Depth: {Depth})", recordKey, depth);

            try
            {
                // 1. Check if existing in target
                if (options.SkipExisting)
                {
                    var existing = await _target.RetrieveAsync(entity.LogicalName,
                        entity.Id, new[] { "modifiedon" }, ct);
                    if (existing != null)
                    {
                        _logger.Information("{Key} already exists. Skipping.", recordKey);
                        return true;
                    }
                }

                // 2. Prepare Entity (Strip system fields, etc.)
                var preparedEntity = PrepareEntityForTarget(entity, options);

                // 3. Handle Special Entity Logic (like Intersect/N:N)
                // TODO: Add Intersect check

                // 4. Try Create/Update with Retry/Fix Strategy
                return await CreateWithFixStrategyAsync(preparedEntity, options, ct);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to sync {Key}", recordKey);
                return false;
            }
        }

        private async Task<bool> CreateWithFixStrategyAsync(
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(() => _target.CreateAsync(entity, ct));
                _logger.Information("Created {Key}:{Id}", entity.LogicalName, entity.Id);
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

            // RECURSIVE PATTERN: Dependency Resolution
            if (msg.Contains("does not exist"))
            {
                return await ResolveMissingDependencyAsync(ex, entity, options, ct);
            }

            // Attribute stripping fixes (legacy logic)
            if (msg.Contains("cannot be modified"))
            {
                return await StripAttributeAndRetryAsync(ex, entity, options, ct);
            }

            if (msg.Contains("is outside the valid range"))
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
            // Regex to extract attribute name from "The property 'name' 
            // cannot be modified" or similar Dataverse error messages.
            var match = System.Text.RegularExpressions.Regex.Match(ex.Message, 
                @"'(\w+)'");
            
            if (match.Success)
            {
                var attrName = match.Groups[1].Value;
                _logger.Warning("Stripping failing attribute '{Attr}' " +
                    "for {Key}:{Id}", attrName, entity.LogicalName, entity.Id);
                
                entity.Attributes.Remove(attrName);
                return await CreateWithFixStrategyAsync(entity, options, ct);
            }

            return false; 
        }

        private async Task<bool> ResolveMissingDependencyAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            CancellationToken ct)
        {
            _logger.Warning("Dependency missing for {Key}:{Id}. " +
                "Message: {Msg}", entity.LogicalName, entity.Id, ex.Message);

            // Regex to match "entityname with Id=guid does not exist"
            var match = System.Text.RegularExpressions.Regex.Match(ex.Message, 
                @"(\w+) with Id=([a-fA-F0-9-]+) does not exist");

            if (match.Success)
            {
                var missingType = match.Groups[1].Value;
                var missingId = Guid.Parse(match.Groups[2].Value);

                _logger.Information("Missing dependency identified: {Type}:{Id}. " +
                    "Attempting recursive sync.", missingType, missingId);

                // Fetch the missing record from source
                var missingRecord = await _source.RetrieveAsync(
                    missingType, missingId, null, ct);

                if (missingRecord != null)
                {
                    // Recursively sync the missing dependency
                    var success = await SyncRecordAsync(missingRecord, options, ct);
                    if (success)
                    {
                        _logger.Information("Dependency {Type}:{Id} synced. " +
                            "Retrying original record {Key}:{Id}.", 
                            missingType, missingId, entity.LogicalName, entity.Id);

                        // Retry the original record creation
                        return await CreateWithFixStrategyAsync(entity, options, ct);
                    }
                }
                else
                {
                    _logger.Error("Could not find missing dependency {Type}:{Id} " +
                        "in source system.", missingType, missingId);
                }
            }

            return false;
        }

        private Entity PrepareEntityForTarget(Entity entity, SyncOptions options)
        {
            var target = new Entity(entity.LogicalName, entity.Id);
            foreach (var attr in entity.Attributes)
            {
                if (IsForbiddenAttribute(attr.Key)) continue;
                target[attr.Key] = attr.Value;
            }
            return target;
        }
        
        private bool IsForbiddenAttribute(string attrName)
        {
            var forbidden = new[] { 
                "createdon", "modifiedon", "versionnumber", 
                "createdby", "modifiedby", "ownerid" 
            };
            return forbidden.Contains(attrName.ToLower());
        }
    }
}
