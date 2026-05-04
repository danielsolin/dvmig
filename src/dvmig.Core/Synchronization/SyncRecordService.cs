using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Polly;
using Polly.Retry;

using CT = System.Threading.CancellationToken;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="ISyncRecordService"/> that orchestrates
   /// the synchronization of individual Dataverse records.
   /// </summary>
   public class SyncRecordService : ISyncRecordService
   {
      private readonly IDataverseProvider _target;
      private readonly IUserResolver _userResolver;
      private readonly ILogger _logger;
      private readonly IRetryService _retryService;
      private readonly IEntityService _entityService;
      private readonly IErrorService _errorService;
      private readonly IDependencyResolver _dependencyResolver;
      private readonly IStatusService _statusService;
      private readonly IMetadataService _metadataService;
      private readonly IFailureService _failureService;
      private readonly ISourceDataService _sourceDataService;
      private readonly ISyncStateService _syncStateService;
      private readonly IRelationshipService _relationshipService;
      private readonly AsyncRetryPolicy _retryPolicy;

      private const int MaxRecursionDepth = 3;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="SyncRecordService"/> class.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="userResolver">The user resolver.</param>
      /// <param name="logger">The logger instance.</param>
      /// <param name="retryService">The retry service.</param>
      /// <param name="entityService">The entity service.</param>
      /// <param name="errorService">The error service.</param>
      /// <param name="dependencyResolver">The dependency resolver.</param>
      /// <param name="statusService">The status service.</param>
      /// <param name="metadataService">The metadata service.</param>
      /// <param name="failureService">The failure service.</param>
      /// <param name="sourceDataService">The source data service.</param>
      /// <param name="syncStateService">The sync state service.</param>
      /// <param name="relationshipService">The relationship service.</param>
      public SyncRecordService(
         IDataverseProvider target,
         IUserResolver userResolver,
         ILogger logger,
         IRetryService retryService,
         IEntityService entityService,
         IErrorService errorService,
         IDependencyResolver dependencyResolver,
         IStatusService statusService,
         IMetadataService metadataService,
         IFailureService failureService,
         ISourceDataService sourceDataService,
         ISyncStateService syncStateService,
         IRelationshipService relationshipService
      )
      {
         _target = target;
         _userResolver = userResolver;
         _logger = logger;
         _retryService = retryService;
         _entityService = entityService;
         _errorService = errorService;
         _dependencyResolver = dependencyResolver;
         _statusService = statusService;
         _metadataService = metadataService;
         _failureService = failureService;
         _sourceDataService = sourceDataService;
         _syncStateService = syncStateService;
         _relationshipService = relationshipService;

         _retryPolicy = _retryService.CreateRetryPolicy();
      }

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

      /// <inheritdoc />
      public async Task<(bool Success, string? FailureMessage)>
         SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            CT ct = default
         )
      {
         bool skipStateCheck = options.ForceResync;

         if (!skipStateCheck && _syncStateService.IsSynced(entity.Id))
         {
            _logger.Debug(
               "Skipping {Entity}:{Id} - Already synced in current state.",
               entity.LogicalName,
               entity.Id
            );

            return (true, string.Empty);
         }

         var recordKey = GetRecordKey(entity);
         if (!_syncStateService.TryEnterRecordScope(
            recordKey,
            MaxRecursionDepth
         ))
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
            _syncStateService.IdMappingCache,
            ct
         );

         var (creatorId, modifierId) = await ResolveAuditUserIdsAsync(
            entity,
            options,
            ct
         );

         await PreserveAuditDataIfRequestedAsync(entity, options, ct);

         var (success, failureMessage) = await CreateWithFixStrategyAsync(
            prepared,
            options,
            creatorId,
            modifierId,
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

      private async Task<(Guid? CreatorId, Guid? ModifierId)>
         ResolveAuditUserIdsAsync(
            Entity sourceEntity,
            SyncOptions options,
            CT ct
         )
      {
         if (!options.PreserveAuditData)
            return (null, null);

         var sourceCreator = sourceEntity.GetAttributeValue<EntityReference>(
            SystemConstants.DataverseAttributes.CreatedBy
         );

         var sourceModifier = sourceEntity.GetAttributeValue<EntityReference>(
            SystemConstants.DataverseAttributes.ModifiedBy
         );

         var creatorId = (await _userResolver.MapUserAsync(
            sourceCreator,
            ct
         ))?.Id;

         var modifierId = (await _userResolver.MapUserAsync(
            sourceModifier,
            ct
         ))?.Id;

         return (creatorId, modifierId);
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
               "Audit data preservation failed for {Entity}:{Id}. " +
               "Continuing sync.",
               entity.LogicalName,
               entity.Id
            );

            _logger.Information("Audit data preservation failed. Continuing...");
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

         _syncStateService.MarkAsSynced(sourceEntity.Id);
         _syncStateService.IdMappingCache[recordKey] = targetEntity.Id;
         _logger.Information(
            $"Synced {sourceEntity.LogicalName}:{sourceEntity.Id}"
         );

         if (!options.PreserveAuditData)
            return;

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

      private static string GetRecordKey(Entity entity)
      {
         return $"{entity.LogicalName}:{entity.Id}";
      }

      #endregion

      #region Error Recovery And Dependency Resolution

      private async Task<(bool success, string failureMessage)>
         HandleSyncExceptionWithRetryAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            Guid? modifierId,
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
               updateFunc: (e, token) => _target.UpdateAsync(e, token, modifierId),
               statusTransitionFunc: (e, opt, token) =>
                  HandleStatusTransitionAsync(e, opt, modifierId, token),
               resolveMissingDependencyFunc: (e, ent, opt, token) =>
                  ResolveMissingDependencyAsync(e, ent, opt, modifierId, token),
               resolveSqlDependencyFunc: (msg, ent, opt, token) =>
                  ResolveSqlDependencyAsync(msg, ent, opt, modifierId, token),
               stripAttributeFunc: (e, ent, opt, token) =>
                  StripAttributeAndRetryAsync(e, ent, opt, modifierId, token),
               findExistingFunc: FindExistingOnTargetAsync
            );

         return success
            ? (true, string.Empty)
            : (false, failureMessage ?? "Unknown error");
      }

      private async Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         SyncOptions options,
         Guid? modifierId,
         CT ct
      )
      {
         return await _statusService.HandleStatusTransitionAsync(
            entity,
            options,
            ct,
            callerId: modifierId
         );
      }

      private async Task<bool> ResolveMissingDependencyAsync(
         Exception ex,
         Entity entity,
         SyncOptions options,
         Guid? modifierId,
         CT ct
      )
      {
         return await _dependencyResolver.ResolveMissingDependencyAsync(
            ex,
            entity,
            options,
            ct,
            syncRecordFunc: SyncRecordAsync,
            retryEntityFunc: (ent, opt, token) =>
               RetryEntityAsync(ent, opt, modifierId, token),
            findExistingFunc: FindExistingOnTargetAsync,
            idMappingCache: _syncStateService.IdMappingCache,
            triedDependencies: _syncStateService.TriedDependencies
         );
      }

      private async Task<bool> ResolveSqlDependencyAsync(
         string errorMessage,
         Entity entity,
         SyncOptions options,
         Guid? modifierId,
         CT ct
      )
      {
         return await _dependencyResolver.ResolveSqlDependencyAsync(
            errorMessage,
            entity,
            options,
            ct,
            syncRecordFunc: SyncRecordAsync,
            retryEntityFunc: (ent, opt, token) =>
               RetryEntityAsync(ent, opt, modifierId, token),
            findExistingFunc: FindExistingOnTargetAsync,
            idMappingCache: _syncStateService.IdMappingCache
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
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _relationshipService.AssociateAsync(entity, ct),
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
               null,
               ct,
               treatAlreadyExistsAsSuccess: true
            );
         }
      }

      private async Task<(bool success, string failureMessage)>
         CreateWithFixStrategyAsync(
            Entity entity,
            SyncOptions options,
            Guid? creatorId,
            Guid? modifierId,
            CT ct
         )
      {
         try
         {
            await _retryPolicy.ExecuteAsync(
               async (ctx) => await _target.CreateAsync(entity, ct, creatorId),
               CreatePollyContext()
            );

            _logger.Information(
               "Created {Key}:{Id}",
               entity.LogicalName,
               entity.Id
            );

            // If modifier is different from creator, we MUST do an update 
            // to ensure ModifiedBy is preserved correctly.
            if (modifierId.HasValue && modifierId != creatorId)
            {
               _logger.Debug(
                  "Modifier {ModifierId} differs from Creator {CreatorId}. " +
                  "Performing update to preserve ModifiedBy.",
                  modifierId,
                  creatorId
               );

               var updateEntity = new Entity(entity.LogicalName, entity.Id);

               await _retryPolicy.ExecuteAsync(
                  async (ctx) => await _target.UpdateAsync(
                     updateEntity,
                     ct,
                     modifierId
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
               entity,
               options,
               modifierId,
               ct
            );
         }
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
         Guid? callerId,
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
            _syncStateService.IdMappingCache,
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
            callerId,
            callerId,
            ct
         );

         return created;
      }

      private async Task<bool> StripAttributeAndRetryAsync(
         Exception ex,
         Entity entity,
         SyncOptions options,
         Guid? modifierId,
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
                  modifierId,
                  modifierId,
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
