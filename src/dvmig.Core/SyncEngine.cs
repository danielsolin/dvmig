using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Polly;
using Polly.Retry;
using Serilog;
using Microsoft.Xrm.Sdk.Query;
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

        private readonly ConcurrentDictionary<string, HashSet<string>>
            _triedDependencies =
                new ConcurrentDictionary<string, HashSet<string>>();

        private readonly ConcurrentDictionary<string, Guid> _idMappingCache =
            new ConcurrentDictionary<string, Guid>();

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

        private TimeSpan GetRetryDelay(
            int retryCount,
            Exception ex,
            Context ctx)
        {
            if (ex.Message.Contains("8004410d"))
            {
                _logger.Information("Service Protection Limit reached. " +
                    "Applying throttled backoff.");

                return TimeSpan.FromSeconds(
                    Math.Min(Math.Pow(2, retryCount), 30));
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

            progress?.Report(
                $"Starting migration of {entities.Count()} records...");

            if (options.UseBulk)
            {
                await SyncBulkAsync(entities, options, progress, ct);
            }
            else
            {
                var semaphore = new SemaphoreSlim(
                    options.MaxDegreeOfParallelism);

                var tasks = entities.Select(async entity =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        await SyncRecordAsync(entity, options, progress, ct);
                        progress?.Report(
                            $"Synced {entity.LogicalName}:{entity.Id}");
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
                var entitiesToProcess = batch;
                if (options.SkipExisting)
                {
                    var existingIds = await CheckExistingIdsAsync(batch, ct);
                    if (existingIds.Any())
                    {
                        _logger.Information(
                            "Skipping {Count} already existing records " +
                            "in batch.",
                            existingIds.Count);

                        entitiesToProcess = batch
                            .Where(e => !existingIds.Contains(e.Id))
                            .ToList();
                    }
                }

                if (!entitiesToProcess.Any())
                {
                    continue;
                }

                _logger.Information(
                    "Processing bulk batch of {Count} records",
                    entitiesToProcess.Count);

                progress?.Report(
                    $"Processing batch of {entitiesToProcess.Count} " +
                    "records...");

                if (options.PreserveDates)
                {
                    try
                    {
                        await _dataPreservation.PreserveDatesBulkAsync(
                            entitiesToProcess,
                            ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(
                            ex,
                            "Bulk date preservation failed. " +
                            "Continuing without date preservation.");

                        progress?.Report(
                            "Date preservation failed. Continuing...");
                    }
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

                foreach (var entity in entitiesToProcess)
                {
                    var metadata = await GetMetadataAsync(
                        entity.LogicalName,
                        ct);

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

                        request.Requests.Add(
                            new CreateRequest { Target = prepared });
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
                            var failedEntity = entitiesToProcess[
                                item.RequestIndex];

                            var errorMsg = item.Fault?.Message ??
                                "Unknown error";

                            _logger.Warning(
                                "Bulk item {Index} failed: {Error}. " +
                                "Shunting {Entity}:{Id} to single sync.",
                                item.RequestIndex,
                                errorMsg,
                                failedEntity.LogicalName,
                                failedEntity.Id);

                            progress?.Report(
                                $"Retrying failed record " +
                                $"{failedEntity.LogicalName}...");
                            await SyncRecordAsync(
                                failedEntity,
                                options,
                                progress,
                                ct);
                        }
                    }
                }
            }
        }

        private async Task<HashSet<Guid>> CheckExistingIdsAsync(
            IEnumerable<Entity> entities,
            CancellationToken ct)
        {
            var first = entities.FirstOrDefault();
            if (first == null)
            {
                return new HashSet<Guid>();
            }

            var logicalName = first.LogicalName;
            var ids = entities.Select(e => e.Id).ToList();

            var query = new QueryExpression(logicalName)
            {
                ColumnSet = new ColumnSet(false), // No columns needed
                NoLock = true
            };

            query.Criteria.AddCondition(
                logicalName + "id",
                ConditionOperator.In,
                ids.Select(id => (object)id).ToArray());

            var existing = await _target.RetrieveMultipleAsync(query, ct);

            return new HashSet<Guid>(existing.Entities.Select(e => e.Id));
        }

        public async Task<bool> SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var recordKey = $"{entity.LogicalName}:{entity.Id}";
            var depth = _recursionTracker.AddOrUpdate(
                recordKey,
                1,
                (_, v) => v + 1);

            if (depth > MaxRecursionDepth)
            {
                _logger.Error(
                    "Max recursion depth reached for {Key}. Skipping.",
                    recordKey);

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

                if (options.SkipExisting)
                {
                    var existing = await _target.RetrieveAsync(
                        entity.LogicalName,
                        entity.Id,
                        new[] { "modifiedon" },
                        ct);

                    if (existing != null)
                    {
                        _idMappingCache[recordKey] = existing.Id;

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
                            entity.Id);

                        progress?.Report(
                            "Date preservation failed. Continuing...");
                    }
                }

                var success = await CreateWithFixStrategyAsync(
                    prepared,
                    options,
                    ct);

                if (success)
                {
                    _idMappingCache[recordKey] = entity.Id;
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
                    () => _target.ExecuteAsync(request, ct));
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
                    () => _target.CreateAsync(entity, ct));

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

            if (msg.Contains("already exists") ||
                msg.Contains("duplicate currency record") ||
                msg.Contains("duplicate key"))
            {
                _logger.Information(
                    "{Key}:{Id} already exists on target. " +
                    "Treating as success.",
                    entity.LogicalName,
                    entity.Id);

                return true;
            }

            if (msg.Contains("does not exist"))
            {
                return await ResolveMissingDependencyAsync(
                    ex, entity, options, ct);
            }

            if (msg.Contains("cannot be modified") ||
                msg.Contains("cannot be set on creation") ||
                msg.Contains("outside the valid range"))
            {
                return await StripAttributeAndRetryAsync(
                    ex, entity, options, ct);
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
            var match = System.Text.RegularExpressions.Regex.Match(
                ex.Message,
                @"'(\w+)'");

            if (match.Success)
            {
                var attrName = match.Groups[1].Value;
                if (entity.Attributes.Contains(attrName))
                {
                    _logger.Warning(
                        "Stripping attribute '{Attr}' for {Key}:{Id}",
                        attrName,
                        entity.LogicalName,
                        entity.Id);

                    entity.Attributes.Remove(attrName);

                    return await CreateWithFixStrategyAsync(
                        entity,
                        options,
                        ct);
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
            // More robust regex to handle both:
            // "Account with Id=GUID does not exist"
            // "Entity 'transactioncurrency' With Id = GUID Does Not Exist"
            var pattern =
                @"(?:Entity )?'?(\w+)'? [Ww]ith Id\s*=\s*([a-fA-F0-9-]+)";

            var match = System.Text.RegularExpressions.Regex.Match(
                ex.Message,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var missingType = match.Groups[1].Value.ToLower();
                var missingId = Guid.Parse(match.Groups[2].Value);
                var recordKey = $"{entity.LogicalName}:{entity.Id}";
                var dependencyKey = $"{missingType}:{missingId}";

                var tried = _triedDependencies.GetOrAdd(
                    recordKey,
                    _ => new HashSet<string>());

                if (tried.Contains(dependencyKey))
                {
                    _logger.Warning(
                        "Already tried to resolve {Dep} for {Record}. " +
                        "Falling back to stripping logic.",
                        dependencyKey,
                        recordKey);
                }
                else
                {
                    tried.Add(dependencyKey);

                    _logger.Information(
                        "Missing dependency {Type}:{Id} detected. " +
                        "Attempting to resolve.",
                        missingType,
                        missingId);

                    var missingRecord = await _source.RetrieveAsync(
                        missingType,
                        missingId,
                        null,
                        ct);

                    if (missingRecord != null)
                    {
                        // Dynamic Mapping: Try to find by business key first
                        var targetId = await FindExistingOnTargetAsync(
                            missingRecord,
                            ct);

                        if (targetId.HasValue)
                        {
                            _logger.Information(
                                "Found {Type} by business key. " +
                                "Mapping {SourceId} -> {TargetId}",
                                missingType,
                                missingId,
                                targetId.Value);

                            _idMappingCache[dependencyKey] = targetId.Value;

                            return await CreateWithFixStrategyAsync(
                                entity,
                                options,
                                ct);
                        }

                        // Normal Sync: Try to sync the record over
                        var success = await SyncRecordAsync(
                            missingRecord,
                            options,
                            null,
                            ct);

                        if (success)
                        {
                            var metadata = await GetMetadataAsync(
                                entity.LogicalName,
                                ct);

                            if (metadata?.IsIntersect == true)
                            {
                                return await SyncIntersectEntityAsync(
                                    entity,
                                    options,
                                    ct);
                            }

                            return await CreateWithFixStrategyAsync(
                                entity,
                                options,
                                ct);
                        }
                    }
                }

                // Fallback: If auto-sync of dependency failed (or not found),
                // strip the attribute and retry the parent record.
                if (options.StripMissingDependencies)
                {
                    var attrToStrip = entity.Attributes
                        .FirstOrDefault(a =>
                            a.Value is EntityReference er &&
                            er.LogicalName == missingType &&
                            er.Id == missingId).Key;

                    if (!string.IsNullOrEmpty(attrToStrip))
                    {
                        _logger.Warning(
                            "Dependency resolution failed for {Type}:{Id}. " +
                            "Stripping attribute '{Attr}' from " +
                            "{ParentType}:{ParentId} and retrying.",
                            missingType,
                            missingId,
                            attrToStrip,
                            entity.LogicalName,
                            entity.Id);

                        entity.Attributes.Remove(attrToStrip);

                        return await CreateWithFixStrategyAsync(
                            entity,
                            options,
                            ct);
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

                if (IsUserAttribute(attr.Key) &&
                    attr.Value is EntityReference userRef)
                {
                    var mapped = await _userMapper.MapUserAsync(userRef, ct);
                    if (mapped != null)
                    {
                        target[attr.Key] = mapped;

                        continue;
                    }

                    // Mapping failed. Do NOT send original user ID to target
                    _logger.Warning(
                        "Skipping user attribute '{Attr}' for {Key}:{Id} " +
                        "because source user could not be mapped.",
                        attr.Key,
                        entity.LogicalName,
                        entity.Id);

                    continue;
                }

                if (metadata?.Attributes != null)
                {
                    var attrMeta = metadata.Attributes
                        .FirstOrDefault(a => a.LogicalName == attr.Key);

                    if (attrMeta != null && attrMeta.IsValidForCreate == false)
                    {
                        _logger.Debug(
                            "Proactively stripping {Attr} for {Entity}",
                            attr.Key,
                            entity.LogicalName);

                        continue;
                    }
                }

                if (attr.Value is EntityReference er)
                {
                    var cacheKey = $"{er.LogicalName}:{er.Id}";
                    if (_idMappingCache.TryGetValue(cacheKey, out var targetId))
                    {
                        target[attr.Key] = new EntityReference(
                            er.LogicalName,
                            targetId);

                        continue;
                    }
                }

                target[attr.Key] = attr.Value;
            }

            return target;
        }

        private async Task<Guid?> FindExistingOnTargetAsync(
            Entity sourceEntity,
            CancellationToken ct)
        {
            var metadata = await GetMetadataAsync(
                sourceEntity.LogicalName,
                ct);

            if (metadata == null)
            {
                return null;
            }

            // 1. Try Alternate Keys
            if (metadata.Keys != null && metadata.Keys.Any())
            {
                foreach (var key in metadata.Keys)
                {
                    var query = new QueryExpression(sourceEntity.LogicalName);
                    query.ColumnSet = new ColumnSet(false);
                    var hasAllParts = true;

                    foreach (var attrName in key.KeyAttributes)
                    {
                        if (!sourceEntity.Contains(attrName))
                        {
                            hasAllParts = false;

                            break;
                        }

                        query.Criteria.AddCondition(
                            attrName,
                            ConditionOperator.Equal,
                            sourceEntity[attrName]);
                    }

                    if (hasAllParts)
                    {
                        var results = await _target
                            .RetrieveMultipleAsync(query, ct);

                        if (results.Entities.Any())
                        {
                            return results.Entities.First().Id;
                        }
                    }
                }
            }

            // 2. Fallback to Primary Name Attribute
            var primaryAttr = metadata.PrimaryNameAttribute;
            if (!string.IsNullOrEmpty(primaryAttr) &&
                sourceEntity.Contains(primaryAttr))
            {
                var query = new QueryExpression(sourceEntity.LogicalName);
                query.ColumnSet = new ColumnSet(false);
                query.Criteria.AddCondition(
                    primaryAttr,
                    ConditionOperator.Equal,
                    sourceEntity[primaryAttr]);

                var results = await _target.RetrieveMultipleAsync(query, ct);
                if (results.Entities.Any())
                {
                    return results.Entities.First().Id;
                }
            }

            return null;
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
                var newMeta = await _target
                    .GetEntityMetadataAsync(logicalName, ct);

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
                "createdon", "modifiedon", "versionnumber",
                "createdby", "modifiedby",
                "createdonbehalfby", "modifiedonbehalfby",
                "overriddencreatedon", "importsequencenumber",
                "address1_addressid", "address2_addressid"
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
