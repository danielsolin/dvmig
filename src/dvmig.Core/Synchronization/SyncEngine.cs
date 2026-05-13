using System.Text.RegularExpressions;

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using static dvmig.Core.Shared.SystemConstants;
using CT = System.Threading.CancellationToken;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Core orchestrator responsible for synchronizing data records between
   /// source and target Dataverse environments, managing both high-level
   /// pagination and record-level resilience.
   /// </summary>
   public class SyncEngine : ISyncEngine
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly IUserService _userService;
      private readonly ILogger _logger;
      private readonly IEntityService _entityService;
      private readonly ISyncStateService _syncStateService;

      private bool? _isSourceDataSupported;
      private const int MaxRecursionDepth = 5;

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncEngine"/> class.
      /// </summary>
      public SyncEngine(
         IDataverseProvider source,
         IDataverseProvider target,
         IUserService userService,
         ILogger logger,
         IEntityService entityService,
         ISyncStateService syncStateService
      )
      {
         _source = source;
         _target = target;
         _userService = userService;
         _logger = logger;
         _entityService = entityService;
         _syncStateService = syncStateService;
      }

      #region Entity Sync Orchestration (Batch)

      /// <inheritdoc />
      public async Task InitializeSyncAsync(CT ct = default)
      {
         await _userService.MapAllSourceUsersAsync(ct);
      }

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

         var columns = await _entityService.GetValidColumnsAsync(
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

         using (
            var semaphore = new SemaphoreSlim(
               parallelOptions.MaxDegreeOfParallelism
            )
         )
         {
            var tasks = entitiesToSync.Select(async entity =>
            {
               await semaphore.WaitAsync(ct);

               try
               {
                  await SyncRecordAndReportAsync(
                     entity,
                     options,
                     recordProgress,
                     ct
                  );
               }
               finally
               {
                  semaphore.Release();
               }
            });

            await Task.WhenAll(tasks);
         }
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
               await LogFailureToTargetAsync(
                  entity,
                  failureMessage ?? "Sync failed.",
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

            await LogFailureToTargetAsync(
               entity,
               FormatFailureMessage("SyncAsync", ex),
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

         if (!_syncStateService.TryEnterRecordScope(
            recordKey,
            MaxRecursionDepth
         ))
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
         var metadata = await _entityService.GetMetadataAsync(
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
            _userService,
            _syncStateService.IdMappingCache,
            ct
         );

         Guid? creatorId = null;
         Guid? modifiedById = null;

         if (options.PreserveAuditData)
         {
            var sourceCreator = entity.GetAttributeValue<EntityReference>(
               DataverseAttributes.CreatedBy
            );

            if (sourceCreator != null)
            {
               var user = await _userService.MapUserAsync(sourceCreator, ct);
               creatorId = user?.Id;
            }

            var sourceModifier = entity.GetAttributeValue<EntityReference>(
               DataverseAttributes.ModifiedBy
            );

            if (sourceModifier != null)
               modifiedById = (await _userService.MapUserAsync(
                  sourceModifier,
                  ct
               ))?.Id;

            creatorId ??= modifiedById;
            modifiedById ??= creatorId;
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
            await CreateSourceDataRecordAsync(_target, entity, ct);
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
            await DeleteSourceDataRecordAsync(
               _target,
               sourceEntity.LogicalName,
               targetEntity.Id,
               ct
            );
         }
      }

      #endregion

      #region Error Handling & Resilience

      public async Task<(bool Success, string FailureMessage)>
         HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            Entity sourceEntity,
            SyncOptions options,
            Guid? creatorId = null,
            Guid? modifiedById = null,
            CT ct = default
         )
      {
         var msg = ex.Message.ToLower();

         bool isDuplicate =
            msg.Contains(ErrorKeywords.AlreadyExists) ||
            msg.Contains(ErrorKeywords.DuplicateCurrency) ||
            msg.Contains(ErrorKeywords.DuplicateKey);

         if (isDuplicate)
            return await HandleDuplicateAsync(entity, modifiedById, ct);

         if (msg.Contains(ErrorKeywords.InvalidStatusCode))
         {
            var success = await HandleStatusTransitionAsync(
               entity,
               sourceEntity,
               options,
               ct,
               modifiedById
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Status transition failed", ex)
            );
         }

         if (msg.Contains(ErrorKeywords.DoesNotExist) ||
             msg.Contains(ErrorKeywords.ForeignKeyConflict))
         {
            var success = await ResolveDependencyAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Dependency resolution failed", ex)
            );
         }

         if (msg.Contains(ErrorKeywords.CannotBeModified) ||
             msg.Contains(ErrorKeywords.CannotBeSetOnCreation) ||
             msg.Contains(ErrorKeywords.OutsideValidRange))
         {
            var success = await StripAttributeAndRetryAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Attribute stripping failed", ex)
            );
         }

         _logger.Error(
            ex,
            "Unresolved error for {Key}:{Id}",
            entity.LogicalName,
            entity.Id
         );

         return (false, FormatFailureMessage("Unresolved error", ex));
      }

      private async Task<(bool Success, string FailureMessage)>
         HandleDuplicateAsync(
            Entity entity,
            Guid? modifiedById,
            CT ct
         )
      {
         _logger.Information(
            "{Key}:{Id} already exists. Attempting update.",
            entity.LogicalName,
            entity.Id
         );

         try
         {
            var targetId = await FindExistingOnTargetAsync(entity, ct);

            if (targetId.HasValue && targetId.Value != entity.Id)
            {
               entity.Id = targetId.Value;

               var pk = await _target.GetPrimaryIdAttributeAsync(
                  entity.LogicalName,
                  ct
               ) ?? $"{entity.LogicalName}id";

               if (entity.Attributes.Contains(pk))
                  entity[pk] = targetId.Value;
            }

            await _target.UpdateAsync(entity, ct, modifiedById);

            return (true, string.Empty);
         }
         catch (Exception updateEx)
         {
            _logger.Warning(
               "Update failed for existing record {Key}:{Id}: {Msg}.",
               entity.LogicalName,
               entity.Id,
               updateEx.Message
            );

            return (true, string.Empty);
         }
      }

      private async Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         CT ct,
         Guid? callerId = null
      )
      {
         var stateValue = entity.Contains(
            DataverseAttributes.StateCode
         )
            ? entity[DataverseAttributes.StateCode]
            : null;

         var statusValue = entity.Contains(
            DataverseAttributes.StatusCode
         )
            ? entity[DataverseAttributes.StatusCode]
            : null;

         entity.Attributes.Remove(DataverseAttributes.StateCode);
         entity.Attributes.Remove(DataverseAttributes.StatusCode);

         var sourceRetry = EntityHelper.Clone(sourceEntity);

         sourceRetry.Attributes.Remove(DataverseAttributes.StateCode);
         sourceRetry.Attributes.Remove(DataverseAttributes.StatusCode);

         var (success, _) = await SyncRecordAsync(
            sourceRetry,
            options.CloneWithForceResync(),
            ct
         );

         if (success && (stateValue != null || statusValue != null))
         {
            try
            {
               var stateOsv = ToOptionSetValue(stateValue);
               var statusOsv = ToOptionSetValue(statusValue);

               if (stateOsv != null)
               {
                  var request = new SetStateRequest
                  {
                     EntityMoniker = entity.ToEntityReference(),
                     State = stateOsv,
                     Status = statusOsv ?? new OptionSetValue(-1)
                  };

                  await _target.ExecuteAsync(request, ct, callerId);
               }
            }
            catch (Exception ex)
            {
               _logger.Warning(
                  "SetState failed for {Key}:{Id}: {Msg}. " +
                  "Trying fallback Update.",
                  entity.LogicalName,
                  entity.Id,
                  ex.Message
               );

               try
               {
                  var fallback = new Entity(entity.LogicalName, entity.Id);

                  if (stateValue != null)
                     fallback[DataverseAttributes.StateCode] =
                        stateValue;

                  if (statusValue != null)
                     fallback[DataverseAttributes.StatusCode] =
                        statusValue;

                  await _target.UpdateAsync(fallback, ct, callerId);
               }
               catch
               {
                  // Final failure ignored
               }
            }
         }

         return success;
      }

      private async Task<bool> ResolveDependencyAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId = null,
         Guid? modifiedById = null,
         CT ct = default
      )
      {
         if (ex.Message.Contains(ErrorKeywords.ForeignKeyConflict))
         {
            var match = Regex.Match(ex.Message, @"column '(\w+)'");

            if (match.Success)
            {
               var columnName = match.Groups[1].Value.ToLower();
               var attr = entity.Attributes
                  .FirstOrDefault(a => a.Key.ToLower() == columnName);

               if (attr.Value is EntityReference er)
                  return await ResolveDependencyInternalAsync(
                     er.LogicalName,
                     er.Id,
                     entity,
                     sourceEntity,
                     options,
                     creatorId,
                     modifiedById,
                     ct
                  );
            }

            return false;
         }

         var pattern = @"(?:Entity )?'?(\w+)'? [Ww]ith " +
            @"Id\s*=\s*([a-fA-F0-9-]+)";
         var m = Regex.Match(ex.Message, pattern, RegexOptions.IgnoreCase);

         if (!m.Success)
            return false;

         var type = m.Groups[1].Value.ToLower();
         var id = Guid.Parse(m.Groups[2].Value);

         return await ResolveDependencyInternalAsync(
            type,
            id,
            entity,
            sourceEntity,
            options,
            creatorId,
            modifiedById,
            ct
         );
      }

      private async Task<bool> ResolveDependencyInternalAsync(
         string type,
         Guid id,
         Entity parent,
         Entity sourceParent,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CT ct
      )
      {
         var parentKey = EntityHelper.GetRecordKey(parent);
         var depKey = EntityHelper.GetRecordKey(type, id);

         var tried = _syncStateService.TriedDependencies.GetOrAdd(
            parentKey,
            _ => new HashSet<string>()
         );

         if (tried.Contains(depKey))
         {
            if (options.StripMissingDependencies)
               return await StripSpecificAttributeAsync(
                  type,
                  id,
                  parent,
                  sourceParent,
                  options,
                  ct
               );

            return false;
         }

         tried.Add(depKey);

         _logger.Information("Resolving missing dependency: {0}", depKey);

         var record = await _source.RetrieveAsync(type, id, null, ct);

         if (record != null)
         {
            var (success, _) = await SyncRecordAsync(record, options, ct);

            if (success)
            {
               var res = await SyncRecordAsync(
                  sourceParent,
                  options.CloneWithForceResync(),
                  ct
               );

               return res.Success;
            }
         }

         if (options.StripMissingDependencies)
            return await StripSpecificAttributeAsync(
               type,
               id,
               parent,
               sourceParent,
               options,
               ct
            );

         return false;
      }

      private async Task<bool> StripSpecificAttributeAsync(
         string type,
         Guid id,
         Entity parent,
         Entity sourceParent,
         SyncOptions options,
         CT ct
      )
      {
         var attr = parent.Attributes
            .FirstOrDefault(a =>
               a.Value is EntityReference er &&
               er.LogicalName == type &&
               er.Id == id
            ).Key;

         if (string.IsNullOrEmpty(attr))
            return false;

         _logger.Warning(
            "Stripping missing dependency '{0}' from {1}",
            attr,
            parent.LogicalName
         );

         parent.Attributes.Remove(attr);
         
         var sourceRetry = EntityHelper.Clone(sourceParent);
         sourceRetry.Attributes.Remove(attr);

         return (await SyncRecordAsync(
            sourceRetry,
            options.CloneWithForceResync(),
            ct
         )).Success;
      }

      private async Task<bool> StripAttributeAndRetryAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CT ct
      )
      {
         var match = Regex.Match(ex.Message, @"'(\w+)'");

         if (match.Success)
         {
            var attr = match.Groups[1].Value;

            if (entity.Attributes.Contains(attr))
            {
               _logger.Warning("Stripping problematic attribute '{0}'", attr);

               entity.Attributes.Remove(attr);
               
               var sourceRetry = EntityHelper.Clone(sourceEntity);
               sourceRetry.Attributes.Remove(attr);

               var res = await SyncRecordAsync(
                  sourceRetry,
                  options.CloneWithForceResync(),
                  ct
               );

               return res.Success;
            }
         }

         return false;
      }

      private OptionSetValue? ToOptionSetValue(object? value)
      {
         if (value == null)
            return null;

         return value is OptionSetValue osv
            ? osv
            : new OptionSetValue((int)value);
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
               DataverseAttributes.CreatedBy
            );

            if (sourceCreator != null)
            {
               var user = await _userService.MapUserAsync(sourceCreator, ct);

               callerId = user?.Id;
            }
         }

         try
         {
            await _entityService.AssociateAsync(_target, entity, ct, callerId);

            _logger.Information("Associated N:N {Key}", entity.LogicalName);

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            if (ex.Message.Contains(ErrorKeywords.AlreadyExists))
               return (true, string.Empty);

            return (false, FormatFailureMessage("AssociateAsync", ex));
         }
      }

      private async Task<(bool Success, string FailureMessage)>
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
            if (_syncStateService.IsSynced(preparedEntity.Id))
            {
               await _target.UpdateAsync(preparedEntity, ct, modifiedById);

               _logger.Information(
                  "Updated (Sync-Completion) {Key}:{Id}",
                  preparedEntity.LogicalName,
                  preparedEntity.Id
               );
            }
            else
            {
               await _target.CreateAsync(preparedEntity, ct, creatorId);

               _logger.Information(
                  "Created {Key}:{Id}",
                  preparedEntity.LogicalName,
                  preparedEntity.Id
               );

               if (modifiedById.HasValue && modifiedById != creatorId)
               {
                  var updateEntity = new Entity(
                     preparedEntity.LogicalName,
                     preparedEntity.Id
                  );

                  await _target.UpdateAsync(updateEntity, ct, modifiedById);
               }
            }

            return (true, string.Empty);
         }
         catch (Exception ex)
         {
            return await HandleSyncExceptionAsync(
               ex,
               preparedEntity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );
         }
      }

      #endregion

      #region Failure Management

      /// <inheritdoc />
      public async Task LogFailureToTargetAsync(
         Entity entity,
         string errorMessage,
         CT ct = default
      )
      {
         try
         {
            var failure = new Entity(
               SystemConstants.MigrationFailure.EntityLogicalName
            );

            var failureName = EntityHelper.GetRecordKey(entity);

            failure[SystemConstants.MigrationFailure.Name] =
               failureName.Length <= 100
                  ? failureName
                  : failureName.Substring(0, 100);

            failure[SystemConstants.MigrationFailure.SourceId] =
               entity.Id.ToString();

            failure[SystemConstants.MigrationFailure.EntityLogicalNameAttr] =
               entity.LogicalName;

            failure[SystemConstants.MigrationFailure.ErrorMessage] =
               errorMessage;

            failure[SystemConstants.MigrationFailure.Timestamp] =
               DateTime.UtcNow;

            await _target.CreateAsync(failure, ct);
         }
         catch (Exception ex)
         {
            _logger.Error(
               ex,
               "Failed to log migration failure for {Entity}:{Id}",
               entity.LogicalName,
               entity.Id
            );
         }
      }

      /// <inheritdoc />
      public async Task<List<MigrationFailureRecord>> GetFailuresAsync(
         IDataverseProvider target,
         string? entityLogicalName = null,
         CT ct = default
      )
      {
         var query = new QueryExpression(
            SystemConstants.MigrationFailure.EntityLogicalName
         )
         {
            ColumnSet = new ColumnSet(
               SystemConstants.MigrationFailure.SourceId,
               SystemConstants.MigrationFailure.EntityLogicalNameAttr,
               SystemConstants.MigrationFailure.ErrorMessage,
               SystemConstants.MigrationFailure.Timestamp
            )
         };

         if (!string.IsNullOrEmpty(entityLogicalName))
            query.Criteria.AddCondition(
               SystemConstants.MigrationFailure.EntityLogicalNameAttr,
               ConditionOperator.Equal,
               entityLogicalName
            );

         query.AddOrder(
            SystemConstants.MigrationFailure.Timestamp,
            OrderType.Ascending
         );

         var result = await target.RetrieveMultipleAsync(query, ct);

         return result.Entities
            .Select(e => new MigrationFailureRecord
            {
               Id = e.Id,
               EntityLogicalName = e.GetAttributeValue<string>(
                  SystemConstants.MigrationFailure.EntityLogicalNameAttr
               ) ?? SystemConstants.MigrationFailure.NotAvailable,
               SourceId = e.GetAttributeValue<string>(
                  SystemConstants.MigrationFailure.SourceId
               ) ?? SystemConstants.MigrationFailure.NotAvailable,
               ErrorMessage = e.GetAttributeValue<string>(
                  SystemConstants.MigrationFailure.ErrorMessage
               ) ?? SystemConstants.MigrationFailure.NotAvailable,
               TimestampUtc = e.GetAttributeValue<DateTime>(
                  SystemConstants.MigrationFailure.Timestamp
               )
            })
            .ToList();
      }

      /// <inheritdoc />
      public async Task ClearFailuresAsync(
         IDataverseProvider target,
         CT ct = default
      )
      {
         var query = new QueryExpression(
            SystemConstants.MigrationFailure.EntityLogicalName
         )
         {
            ColumnSet = new ColumnSet(false)
         };

         var result = await target.RetrieveMultipleAsync(query, ct);

         foreach (var entity in result.Entities)
         {
            ct.ThrowIfCancellationRequested();
            await target.DeleteAsync(
               SystemConstants.MigrationFailure.EntityLogicalName,
               entity.Id,
               ct
            );
         }
      }

      /// <inheritdoc />
      public async Task<bool> IsFailureLoggingInitializedAsync(
         IDataverseProvider target,
         CT ct = default
      )
      {
         var meta = await target.GetEntityMetadataAsync(
            SystemConstants.MigrationFailure.EntityLogicalName,
            ct
         );

         return meta != null;
      }

      #endregion

      #region Audit Data Preservation (Internal)

      private async Task CreateSourceDataRecordAsync(
         IDataverseProvider target,
         Entity sourceEntity,
         CT ct = default
      )
      {
         if (!await CheckSourceDataEntityExistsAsync(target, ct))
            return;

         bool hasAuditData =
            sourceEntity.Contains(DataverseAttributes.CreatedOn) ||
            sourceEntity.Contains(DataverseAttributes.ModifiedOn);

         if (!hasAuditData)
            return;

         var sourceData = new Entity(
            SystemConstants.SourceData.EntityLogicalName
         );

         sourceData[SystemConstants.SourceData.EntityId] =
            sourceEntity.Id.ToString();

         sourceData[SystemConstants.SourceData.EntityLogicalNameAttr] =
            sourceEntity.LogicalName.ToLowerInvariant();

         if (sourceEntity.Contains(DataverseAttributes.CreatedOn))
            sourceData[SystemConstants.SourceData.CreatedOn] =
               sourceEntity[DataverseAttributes.CreatedOn];

         if (sourceEntity.Contains(DataverseAttributes.ModifiedOn))
            sourceData[SystemConstants.SourceData.ModifiedOn] =
               sourceEntity[DataverseAttributes.ModifiedOn];

         try
         {
            await target.CreateAsync(sourceData, ct);
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to create source data record for {Entity}:{Id}",
               sourceEntity.LogicalName,
               sourceEntity.Id
            );
         }
      }

      private async Task DeleteSourceDataRecordAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CT ct = default
      )
      {
         if (!await CheckSourceDataEntityExistsAsync(target, ct))
            return;

         try
         {
            var entityName = SystemConstants.SourceData.EntityLogicalName;
            var primaryId = SystemConstants.SourceData.PrimaryId;
            var sourceEntityId = SystemConstants.SourceData.EntityId;
            var logicalNameAttr =
               SystemConstants.SourceData.EntityLogicalNameAttr;

            var fetchXml = $@"
<fetch version='1.0' output-format='xml-platform' mapping='logical' 
       distinct='false' count='1'>
  <entity name='{entityName}'>
    <attribute name='{primaryId}' />
    <filter type='and'>
      <condition attribute='{sourceEntityId}' operator='eq' 
                 value='{entityId}' />
      <condition attribute='{logicalNameAttr}' operator='eq' 
                 value='{logicalName.ToLowerInvariant()}' />
    </filter>
  </entity>
</fetch>";

            var result = await target.RetrieveMultipleAsync(
               new FetchExpression(fetchXml),
               ct
            );

            if (result.Entities.Any())
               await target.DeleteAsync(
                  SystemConstants.SourceData.EntityLogicalName,
                  result.Entities[0].Id,
                  ct
               );
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to delete source data record for {Entity}:{Id}",
               logicalName,
               entityId
            );
         }
      }

      private async Task<bool> CheckSourceDataEntityExistsAsync(
         IDataverseProvider target,
         CT ct
      )
      {
         if (_isSourceDataSupported.HasValue)
            return _isSourceDataSupported.Value;

         try
         {
            var meta = await target.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               ct
            );

            _isSourceDataSupported = meta != null;
         }
         catch
         {
            _isSourceDataSupported = false;
         }

         if (_isSourceDataSupported == false)
            _logger.Warning(
               "Source data preservation entity '{Entity}' not found. " +
               "Audit data preservation will be disabled.",
               SystemConstants.SourceData.EntityLogicalName
            );

         return _isSourceDataSupported.Value;
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
            ct
         );
      }

      private string FormatFailureMessage(string context, Exception ex)
      {
         var baseEx = ex.GetBaseException();

         return $"[{context}] {baseEx.Message}";
      }

      #endregion
   }
}
