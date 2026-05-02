using dvmig.Core.Interfaces;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;

using CT = System.Threading.CancellationToken;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Core engine responsible for synchronizing entities from the source
   /// to the target Dataverse environment. Handles dependency resolution,
   /// retries, and data transformations.
   /// </summary>
   public partial class SyncEngine : ISyncEngine
   {
      #region Fields And State
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly IUserResolver _userResolver;
      private readonly ISyncStateService _stateService;
      private readonly ILogger _logger;
      private readonly IRetryService _retryService;
      private readonly IEntityService _entityService;
      private readonly IErrorService _errorService;
      private readonly IDependencyResolver _dependencyResolver;
      private readonly IStatusService _statusService;
      private readonly IMetadataService _metadataService;
      private readonly IFailureService _failureService;
      private readonly ISourceDateService _sourceDateService;
      private readonly AsyncRetryPolicy _retryPolicy;

      private readonly ConcurrentDictionary<string, int> _recursionTracker =
         new ConcurrentDictionary<string, int>();

      private readonly ConcurrentDictionary<string, HashSet<string>>
         _triedDependencies =
            new ConcurrentDictionary<string, HashSet<string>>();

      private readonly ConcurrentDictionary<string, Guid> _idMappingCache =
         new ConcurrentDictionary<string, Guid>();

      private ConcurrentDictionary<Guid, byte> _syncedIds =
         new ConcurrentDictionary<Guid, byte>();

      private const int MaxRecursionDepth = 3;
      #endregion

      public SyncEngine(
         IDataverseProvider source,
         IDataverseProvider target,
         IUserResolver userResolver,
         ISyncStateService stateService,
         ILogger logger,
         IRetryService retryService,
         IEntityService entityService,
         IErrorService errorService,
         IDependencyResolver dependencyResolver,
         IStatusService statusService,
         IMetadataService metadataService,
         IFailureService failureService,
         ISourceDateService sourceDateService
      )
      {
         _source = source;
         _target = target;
         _userResolver = userResolver;
         _stateService = stateService;
         _logger = logger;
         _retryService = retryService;
         _entityService = entityService;
         _errorService = errorService;
         _dependencyResolver = dependencyResolver;
         _statusService = statusService;
         _metadataService = metadataService;
         _failureService = failureService;
         _sourceDateService = sourceDateService;

         _retryPolicy = _retryService.CreateRetryPolicy();
      }

      #region Public Sync API
      public async Task InitializeEntitySyncAsync(string logicalName)
      {
         await _stateService.InitializeAsync(
            _source.ConnectionString,
            _target.ConnectionString,
            logicalName
         );

         var ids = await _stateService.GetSyncedIdsAsync();
         _syncedIds = new ConcurrentDictionary<Guid, byte>(
            ids.Select(id => new KeyValuePair<Guid, byte>(id, 1))
         );
      }

      public async Task SyncEntityAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         await InitializeEntitySyncAsync(logicalName);

         var columns = await GetValidColumnsAsync(logicalName, ct);
         var syncQuery = query ?? new QueryExpression(logicalName)
         {
            ColumnSet = columns
         };

         syncQuery.PageInfo = new PagingInfo
         {
            Count = 500,
            PageNumber = 1
         };

         int totalSynced = 0;

         while (true)
         {
            var response = await _source.RetrieveMultipleAsync(
               syncQuery,
               ct
            );
            if (response.Entities.Count == 0)
               break;

            var count = response.Entities.Count;
            var recordCountSoFar = totalSynced + count;

            _logger.Information(
               $"Syncing {logicalName} page " +
               $"{syncQuery.PageInfo.PageNumber} " +
               $"({recordCountSoFar} records " +
               $"found so far)..."
            );

            await SyncAsync(
               response.Entities,
               options,
               recordProgress,
               ct
            );

            totalSynced += response.Entities.Count;

            if (!response.MoreRecords)
               break;

            syncQuery.PageInfo.PageNumber++;
            syncQuery.PageInfo.PagingCookie = response.PagingCookie;
         }

         _logger.Information(
            "SyncEntity {Entity} finished. Total records: {Count}",
            logicalName,
            totalSynced
         );

         _triedDependencies.Clear();
         _idMappingCache.Clear();
      }

      public async Task SyncAsync(
         IEnumerable<Entity> entities,
         SyncOptions options,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         var entitiesToSync = entities
            .Where(e => !_syncedIds.ContainsKey(e.Id))
            .ToList();

         if (!entitiesToSync.Any())
            return;

         _recursionTracker.Clear();

         var parallelOptions = new ParallelOptions
         {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
         };

         await Parallel.ForEachAsync(
            entitiesToSync,
            parallelOptions,
            async (entity, token) => await SyncRecordAndReportAsync(
               entity,
               options,
               recordProgress,
               token
            )
         );
      }

      #endregion

      #region Record Sync Workflow

      private async Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CT ct
      )
      {
         try
         {
            var (success, failureMessage) = await SyncRecordAsync(
               entity,
               options,
               ct
            );

            if (!success)
            {
               var errorMsg = failureMessage ??
                  "Sync failed during record processing.";

               await LogFailureWithRetryAsync(
                  entity,
                  errorMsg,
                  ct
               );
            }

            recordProgress?.Report(success);
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            _logger.Error(
               ex,
               "Error syncing {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );

            var failureMessage = _errorService.FormatFailureMessage(
               "SyncAsync",
               ex
            );

            await LogFailureWithRetryAsync(
               entity,
               failureMessage,
               ct
            );

            recordProgress?.Report(false);
         }
      }

      public async Task<(bool Success, string? FailureMessage)>
         SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            CT ct = default
         )
      {
         bool skipStateCheck = options is ReconciliationSyncOptions;

         if (!skipStateCheck && _syncedIds.ContainsKey(entity.Id))
         {
            _logger.Debug(
               "Skipping {Entity}:{Id} - Already synced in current state.",
               entity.LogicalName,
               entity.Id
            );

            return (true, string.Empty);
         }

         var recordKey = GetRecordKey(entity);
         if (!TryEnterRecordScope(recordKey))
            return (false, "Max recursion depth reached.");

         try
         {
            return await SyncRecordCoreAsync(
               entity,
               options,
               ct
            );
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            _logger.Error(ex, "Failed to sync {Key}", recordKey);
            _logger.Information(
               $"FAILED {entity.LogicalName}:{entity.Id} - " +
               $"{ex.Message}"
            );

            return (false, _errorService.FormatFailureMessage(
               "SyncRecordAsync",
               ex
            ));
         }
         finally
         {
            LeaveRecordScope(recordKey);
         }
      }

      private async Task<(bool Success, string? FailureMessage)>
         SyncRecordCoreAsync(
            Entity entity,
            SyncOptions options,
            CT ct
         )
      {
         var metadata = await _metadataService.GetMetadataAsync(
            entity.LogicalName,
            ct
         );

         if (metadata == null)
            return MetadataMissing(entity);

         if (metadata.IsIntersect == true)
            return await SyncIntersectEntityAsync(
               entity,
               options,
               ct
            );

         var prepared = await _entityService.PrepareEntityForTargetAsync(
            entity,
            metadata,
            options,
            _userResolver,
            _idMappingCache,
            ct
         );

         await PreserveDatesIfRequestedAsync(entity, options, ct);

         var (success, failureMessage) = await CreateWithFixStrategyAsync(
            prepared,
            options,
            ct
         );

         if (!success)
            return (
               false,
               failureMessage ??
                  $"Failed to sync {entity.LogicalName}:{entity.Id}."
            );

         await CompleteSuccessfulSyncAsync(
            sourceEntity: entity,
            targetEntity: prepared,
            options,
            ct
         );

         return (true, string.Empty);
      }

      private bool TryEnterRecordScope(string recordKey)
      {
         var depth = _recursionTracker.AddOrUpdate(
            recordKey,
            1,
            (_, v) => v + 1
         );

         if (depth <= MaxRecursionDepth)
            return true;

         _logger.Error(
            "Max recursion depth reached for {Key}. Skipping.",
            recordKey
         );

         LeaveRecordScope(recordKey);

         return false;
      }

      private void LeaveRecordScope(string recordKey)
      {
         _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);
      }

      private (bool Success, string FailureMessage) MetadataMissing(
         Entity entity
      )
      {
         _logger.Error(
            "Metadata could not be retrieved for {Entity}. " +
            "Skipping record {Id}.",
            entity.LogicalName,
            entity.Id
         );

         return (
            false,
            "Metadata could not be retrieved for " + entity.LogicalName
         );
      }

      private async Task PreserveDatesIfRequestedAsync(
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         if (!options.PreserveDates)
            return;

         try
         {
            await _sourceDateService.CreateSourceDateRecordAsync(
               _target,
               entity,
               ct
            );
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

            _logger.Information("Date preservation failed. Continuing...");
         }
      }

      private async Task CompleteSuccessfulSyncAsync(
         Entity sourceEntity,
         Entity targetEntity,
         SyncOptions options,
         CT ct
      )
      {
         var recordKey = GetRecordKey(sourceEntity);

         _syncedIds.TryAdd(sourceEntity.Id, 1);
         _idMappingCache[recordKey] = targetEntity.Id;
         _logger.Information(
            $"Synced {sourceEntity.LogicalName}:{sourceEntity.Id}"
         );

         await _stateService.MarkAsSyncedAsync(
            sourceEntity.LogicalName,
            sourceEntity.Id
         );

         if (!options.PreserveDates)
            return;

         await _retryPolicy.ExecuteAsync(
            async (ctx) => await _sourceDateService.DeleteSourceDateRecordAsync(
               _target,
               sourceEntity.LogicalName,
               targetEntity.Id,
               ct
            ),
            CreatePollyContext()
         );
      }

      private static string GetRecordKey(Entity entity)
      {
         return $"{entity.LogicalName}:{entity.Id}";
      }

      #endregion

      #region Association Request Helpers

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

      #endregion

      #region Error Recovery And Dependency Resolution

      private async Task<(bool success, string failureMessage)>
         HandleSyncExceptionWithRetryAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            CT ct,
            bool treatAlreadyExistsAsSuccess = false
         )
      {
         if (treatAlreadyExistsAsSuccess &&
            ex.Message.Contains(SystemConstants.ErrorKeywords.AlreadyExists))
            return (true, string.Empty);

         var (success, failureMessage) =
            await _errorService.HandleSyncExceptionAsync(
               ex,
               entity,
               options,
               ct,
               updateFunc: _target.UpdateAsync,
               statusTransitionFunc: HandleStatusTransitionAsync,
               resolveMissingDependencyFunc: ResolveMissingDependencyAsync,
               resolveSqlDependencyFunc: ResolveSqlDependencyAsync,
               stripAttributeFunc: StripAttributeAndRetryAsync,
               findExistingFunc: FindExistingOnTargetAsync
            );

         return success
            ? (true, string.Empty)
            : (false, failureMessage ?? "Unknown error");
      }

      private async Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         return await _statusService.HandleStatusTransitionAsync(
            entity,
            options,
            ct
         );
      }

      private async Task<bool> ResolveMissingDependencyAsync(
         Exception ex,
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         return await _dependencyResolver.ResolveMissingDependencyAsync(
            ex,
            entity,
            options,
            ct,
            syncRecordFunc: SyncRecordAsync,
            retryEntityFunc: RetryEntityAsync,
            findExistingFunc: FindExistingOnTargetAsync,
            idMappingCache: _idMappingCache,
            triedDependencies: _triedDependencies
         );
      }

      private async Task<bool> ResolveSqlDependencyAsync(
         string errorMessage,
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         return await _dependencyResolver.ResolveSqlDependencyAsync(
            errorMessage,
            entity,
            options,
            ct,
            syncRecordFunc: SyncRecordAsync,
            retryEntityFunc: RetryEntityAsync,
            findExistingFunc: FindExistingOnTargetAsync,
            idMappingCache: _idMappingCache
         );
      }

      #endregion

      #region Create And Associate Operations

      private async Task<(bool success, string failureMessage)>
         SyncIntersectEntityAsync(
            Entity entity,
            SyncOptions options,
            CT ct
         )
      {
         try
         {
            var request = CreateAssociateRequest(entity);
            if (request == null)
               return (false, "Invalid N:N relationship record.");

            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _target.ExecuteAsync(request, ct),
               CreatePollyContext()
            );

            _logger.Information(
               "Associated N:N relationship {Key}",
               entity.LogicalName
            );

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionWithRetryAsync(
               ex,
               entity,
               options,
               ct,
               treatAlreadyExistsAsSuccess: true
            );
         }
      }

      private async Task<(bool success, string failureMessage)>
         CreateWithFixStrategyAsync(
            Entity entity,
            SyncOptions options,
            CT ct
         )
      {
         try
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _target.CreateAsync(entity, ct),
               CreatePollyContext()
            );

            _logger.Information(
               "Created {Key}:{Id}",
               entity.LogicalName,
               entity.Id
            );

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionWithRetryAsync(
               ex,
               entity,
               options,
               ct
            );
         }
      }

      #endregion

      #region Public Metadata API

      public async Task<ColumnSet> GetValidColumnsAsync(
         string logicalName,
         CT ct = default
      )
      {
         return await _metadataService.GetValidColumnsAsync(logicalName, ct);
      }

      #endregion

      #region Dependency Retry Helpers

      private async Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         CT ct
      )
      {
         return await _entityService.FindExistingOnTargetAsync(
            entity,
            _target,
            _metadataService.GetMetadataAsync,
            ct
         );
      }

      private async Task<bool> RetryEntityAsync(
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         var metadata = await _metadataService.GetMetadataAsync(
            entity.LogicalName,
            ct
         );
         if (metadata == null)
            return false;

         var prepared = await _entityService.PrepareEntityForTargetAsync(
            entity,
            metadata,
            options,
            _userResolver,
            _idMappingCache,
            ct
         );

         if (metadata?.IsIntersect == true)
         {
            var (success, _) = await SyncIntersectEntityAsync(
               prepared,
               options,
               ct
            );

            return success;
         }

         var (created, _) = await CreateWithFixStrategyAsync(
            prepared,
            options,
            ct
         );

         return created;
      }

      private async Task<bool> StripAttributeAndRetryAsync(
         Exception ex,
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         var match = Regex.Match(
            ex.Message,
            @"'(\w+)'"
         );

         if (match.Success)
         {
            var attrName = match.Groups[1].Value;
            if (entity.Attributes.Contains(attrName))
            {
               _logger.Warning(
                  "Stripping attribute '{Attr}' for {Key}:{Id}",
                  attrName,
                  entity.LogicalName,
                  entity.Id
               );

               _logger.Information(
                  $"Stripping attribute '{attrName}' and retrying..."
               );

               entity.Attributes.Remove(attrName);

               var (success, _) = await CreateWithFixStrategyAsync(
                  entity,
                  options,
                  ct
               );

               return success;
            }
         }

         return false;
      }

      #endregion
   
      #region Retry And Failure Logging
      private Context CreatePollyContext()
      {
         var context = new Context();
         
         return context;
      }

      private async Task LogFailureWithRetryAsync(
         Entity entity,
         string failureMessage,
         CT ct
      )
      {
         var pollyCtx = CreatePollyContext();

         try
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _failureService.LogFailureToTargetAsync(
                  entity,
                  failureMessage,
                  ct
               ),
               pollyCtx
            );
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception logEx)
         {
            _logger.Error(
               logEx,
               "Failed to persist failure log for {Entity}:{Id}.",
               entity.LogicalName,
               entity.Id
            );

            _logger.Information(
               $"{SystemConstants.UiMarkup.Red}ERROR[/] " +
               $"Could not log migration failure: {logEx.Message}"
            );
         }
      }
      #endregion
   }
}
