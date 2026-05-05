using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;

using CT = System.Threading.CancellationToken;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Core orchestrator responsible for synchronizing data records between
   /// source and target Dataverse environments, managing both high-level
   /// pagination and granular record-level resilience.
   /// </summary>
   public class SyncEngine : ISyncEngine
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly IUserResolver _userResolver;
      private readonly ILogger _logger;
      private readonly IEntityService _entityService;
      private readonly ISyncResilienceService _resilience;
      private readonly IMetadataService _metadataService;
      private readonly IFailureService _failureService;
      private readonly ISourceDataService _sourceDataService;
      private readonly ISyncStateService _syncStateService;
      private readonly IRelationshipService _relationshipService;
      private readonly AsyncRetryPolicy _retryPolicy;

      private const int MaxRecursionDepth = 3;

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncEngine"/> class.
      /// </summary>
      public SyncEngine(
         IDataverseProvider source,
         IDataverseProvider target,
         IUserResolver userResolver,
         ILogger logger,
         IEntityService entityService,
         ISyncResilienceService resilience,
         IMetadataService metadataService,
         IFailureService failureService,
         ISourceDataService sourceDataService,
         ISyncStateService syncStateService,
         IRelationshipService relationshipService
      )
      {
         _source = source;
         _target = target;
         _userResolver = userResolver;
         _logger = logger;
         _entityService = entityService;
         _resilience = resilience;
         _metadataService = metadataService;
         _failureService = failureService;
         _sourceDataService = sourceDataService;
         _syncStateService = syncStateService;
         _relationshipService = relationshipService;

         _retryPolicy = _resilience.CreateRetryPolicy();

         _resilience.SetEngine(this);
      }

      #region Entity Sync Orchestration (Batch)

      /// <inheritdoc />
      public async Task InitializeEntitySyncAsync(
         string logicalName,
         CT ct = default
      )
      {
         var ids = await _entityService.GetAllIdsAsync(
            _target,
            logicalName,
            ct
         );

         _syncStateService.InitializeSyncedIds(ids);
      }

      /// <inheritdoc />
      public async Task SyncAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         await InitializeEntitySyncAsync(logicalName, ct);

         var columns = await _metadataService.GetValidColumnsAsync(
            logicalName,
            ct
         );

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

            await ProcessBatchAsync(
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

         _syncStateService.ClearState();
      }

      private async Task ProcessBatchAsync(
         IEnumerable<Entity> entities,
         SyncOptions options,
         IProgress<bool>? recordProgress = null,
         CT ct = default
      )
      {
         var entitiesToSync = options.ForceResync
            ? entities.ToList()
            : entities
               .Where(e => !_syncStateService.IsSynced(e.Id))
               .ToList();

         if (!entitiesToSync.Any())
            return;

         var parallelOptions = new ParallelOptions
         {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
         };

         await Parallel.ForEachAsync(
            entitiesToSync,
            parallelOptions,
            async (entity, token) =>
            {
               await SyncRecordAndReportAsync(
                  entity,
                  options,
                  recordProgress,
                  token
               );
            }
         );
      }

      #endregion

      #region Record Sync Workflow

      /// <inheritdoc />
      public async Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CT ct = default
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
               await LogFailureWithRetryAsync(
                  entity,
                  failureMessage ?? "Sync failed.",
                  ct
               );
            }

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

            await LogFailureWithRetryAsync(
               entity,
               _resilience.FormatFailureMessage("SyncAsync", ex),
               ct
            );

            recordProgress?.Report(false);
         }
      }

      /// <inheritdoc />
      public async Task<(bool Success, string? FailureMessage)>
         SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            CT ct = default
         )
      {
         if (!options.ForceResync && _syncStateService.IsSynced(entity.Id))
            return (true, string.Empty);

         var recordKey = EntityHelper.GetRecordKey(entity);

         if (!_syncStateService.TryEnterRecordScope(recordKey, MaxRecursionDepth))
            return (false, "Max recursion depth reached.");

         try
         {
            return await SyncRecordCoreAsync(entity, options, ct);
         }
         finally
         {
            _syncStateService.LeaveRecordScope(recordKey);
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
            return (false, $"Metadata missing for {entity.LogicalName}");

         if (metadata.IsIntersect == true)
            return await SyncIntersectEntityAsync(entity, options, ct);

         var prepared = await _entityService.PrepareEntityForTargetAsync(
            entity,
            metadata,
            options,
            _userResolver,
            _syncStateService.IdMappingCache,
            ct
         );

         Guid? creatorId = null;
         Guid? modifiedById = null;

         if (options.PreserveAuditData)
         {
            var sourceCreator = entity.GetAttributeValue<EntityReference>(
               SystemConstants.DataverseAttributes.CreatedBy
            );

            if (sourceCreator != null)
            {
               creatorId = (await _userResolver.MapUserAsync(
                  sourceCreator,
                  ct
               ))?.Id;
            }

            var sourceModifier = entity.GetAttributeValue<EntityReference>(
               SystemConstants.DataverseAttributes.ModifiedBy
            );

            if (sourceModifier != null)
            {
               modifiedById = (await _userResolver.MapUserAsync(
                  sourceModifier,
                  ct
               ))?.Id;
            }

            if (creatorId == null)
               creatorId = modifiedById;

            if (modifiedById == null)
               modifiedById = creatorId;
         }

         await PreserveAuditDataIfRequestedAsync(entity, options, ct);

         var (success, failureMessage) = await CreateWithFixStrategyAsync(
            prepared,
            entity,
            options,
            creatorId,
            modifiedById,
            ct
         );

         if (!success)
         {
            return (
               false,
               failureMessage ??
                  $"Failed to sync {EntityHelper.GetRecordKey(entity)}."
            );
         }

         await CompleteSuccessfulSyncAsync(
            sourceEntity: entity,
            targetEntity: prepared,
            options,
            ct
         );

         return (true, string.Empty);
      }

      private async Task PreserveAuditDataIfRequestedAsync(
         Entity entity,
         SyncOptions options,
         CT ct
      )
      {
         if (!options.PreserveAuditData)
            return;

         try
         {
            await _sourceDataService.CreateSourceDataRecordAsync(
               _target,
               entity,
               _userResolver,
               ct
            );
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Audit data preservation failed for {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );
         }
      }

      private async Task CompleteSuccessfulSyncAsync(
         Entity sourceEntity,
         Entity targetEntity,
         SyncOptions options,
         CT ct
      )
      {
         var recordKey = EntityHelper.GetRecordKey(sourceEntity);

         _syncStateService.MarkAsSynced(sourceEntity.Id);
         _syncStateService.IdMappingCache[recordKey] = targetEntity.Id;

         _logger.Information(
            "Synced {Key}:{Id}",
            sourceEntity.LogicalName,
            sourceEntity.Id
         );

         if (options.PreserveAuditData)
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _sourceDataService
                  .DeleteSourceDataRecordAsync(
                     _target,
                     sourceEntity.LogicalName,
                     targetEntity.Id,
                     ct
                  ),
               CreatePollyContext()
            );
         }
      }

      #endregion

      #region Error Recovery Helpers

      private async Task<(bool success, string failureMessage)>
         HandleSyncExceptionWithRetryAsync(
            Exception ex,
            Entity entity,
            Entity sourceEntity,
            SyncOptions options,
            Guid? creatorId,
            Guid? modifiedById,
            bool treatAlreadyExistsAsSuccess = false
         )
      {
         if (treatAlreadyExistsAsSuccess &&
            ex.Message.Contains(SystemConstants.ErrorKeywords.AlreadyExists))
            return (true, string.Empty);

         var (success, failureMessage) =
            await _resilience.HandleSyncExceptionAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               default
            );

         return (success, failureMessage ?? "Unknown error");
      }

      #endregion

      #region Create and Associate Operations

      private async Task<(bool success, string failureMessage)>
         SyncIntersectEntityAsync(
            Entity entity,
            SyncOptions options,
            CT ct
         )
      {
         Guid? callerId = null;

         if (options.PreserveAuditData)
         {
            var sourceCreator = entity.GetAttributeValue<EntityReference>(
               SystemConstants.DataverseAttributes.CreatedBy
            );

            if (sourceCreator != null)
               callerId = (await _userResolver.MapUserAsync(sourceCreator, ct))?
                  .Id;
         }

         try
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _relationshipService.AssociateAsync(
                  entity,
                  ct
               ),
               CreatePollyContext()
            );

            _logger.Information(
               "Associated N:N {Key}",
               entity.LogicalName
            );

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionWithRetryAsync(
               ex,
               entity,
               entity,
               options,
               callerId,
               callerId,
               treatAlreadyExistsAsSuccess: true
            );
         }
      }

      private async Task<(bool success, string failureMessage)>
         CreateWithFixStrategyAsync(
            Entity preparedEntity,
            Entity sourceEntity,
            SyncOptions options,
            Guid? creatorId,
            Guid? modifiedById,
            CT ct
         )
      {
         try
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _target.CreateAsync(
                  preparedEntity,
                  ct,
                  creatorId
               ),
               CreatePollyContext()
            );

            _logger.Information(
               "Created {Key}:{Id}",
               preparedEntity.LogicalName,
               preparedEntity.Id
            );

            // If modifier is different from creator, we MUST do an update 
            // to ensure ModifiedBy is preserved correctly for NEW records.
            if (modifiedById.HasValue && modifiedById != creatorId)
            {
               _logger.Debug(
                  "Modifier {ModifierId} differs from Creator {CreatorId}. " +
                  "Performing update to preserve ModifiedBy.",
                  modifiedById.Value,
                  creatorId?.ToString() ?? "NULL"
               );

               var updateEntity = new Entity(
                  preparedEntity.LogicalName,
                  preparedEntity.Id
               );

               await _retryPolicy.ExecuteAsync(
                  async (ctx) => await _target.UpdateAsync(
                     updateEntity,
                     ct,
                     modifiedById
                  ),
                  CreatePollyContext()
               );
            }

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionWithRetryAsync(
               ex,
               preparedEntity,
               sourceEntity,
               options,
               creatorId,
               modifiedById
            );
         }
      }

      #endregion

      #region Private Helpers

      /// <inheritdoc />
      public async Task<Guid?> FindExistingOnTargetAsync(
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

      private Context CreatePollyContext() => new Context();

      private async Task LogFailureWithRetryAsync(
         Entity entity,
         string failureMessage,
         CT ct
      )
      {
         try
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _failureService.LogFailureToTargetAsync(
                  entity,
                  failureMessage,
                  ct
               ),
               CreatePollyContext()
            );
         }
         catch (Exception logEx)
         {
            _logger.Error(
               logEx,
               "Failed to log failure for {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );
         }
      }

      #endregion
   }
}
