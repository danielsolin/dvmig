using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of the reconciliation service.
   /// </summary>
   public class ReconciliationService : IReconciliationService
   {
      private readonly IEntityService _entityService;
      private readonly ILogger _logger;

      public ReconciliationService(IEntityService entityService, ILogger logger)
      {
         _entityService = entityService;
         _logger = logger;
      }
      /// <inheritdoc />
      public async Task<bool> IsInitializedAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      )
      {
         var meta = await target.GetEntityMetadataAsync(
             SystemConstants.MigrationFailure.EntityLogicalName,
             ct
         );

         return meta != null;
      }

      /// <inheritdoc />
      public async Task<List<MigrationFailureRecord>> GetFailuresAsync(
          IDataverseProvider target,
          string? entityLogicalName = null,
          CancellationToken ct = default
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

         return result.Entities.Select(e => new MigrationFailureRecord
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
         }).ToList();
      }

      /// <inheritdoc />
      public async Task ClearFailuresAsync(
          IDataverseProvider target,
          CancellationToken ct = default
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
      public async Task DeleteFailureAsync(
          IDataverseProvider target,
          Guid failureId,
          CancellationToken ct = default
      )
      {
         await target.DeleteAsync(
             SystemConstants.MigrationFailure.EntityLogicalName,
             failureId,
             ct
         );
      }

      /// <inheritdoc />
      public async Task PerformReconciliationAsync(
          string logicalName,
          IDataverseProvider source,
          IDataverseProvider target,
          ISyncEngine engine,
          SyncOptions options,
          IProgress<(int Processed, int Total, bool Success)>? progress = null,
          CancellationToken ct = default
      )
      {
         _logger.Information($"Starting reconciliation for {logicalName}...");

         // Ensure the engine is initialized for this entity
         await engine.InitializeEntitySyncAsync(logicalName);

         var sourceIds = await _entityService.GetAllIdsAsync(
            source,
            logicalName,
            ct
         );

         var targetIds = await _entityService.GetAllIdsAsync(
            target,
            logicalName,
            ct
         );

         var missingIds = sourceIds.Except(targetIds).ToList();
         var total = missingIds.Count;

         if (total == 0)
         {
            _logger.Information(
               $"Reconciliation complete for {logicalName}. " +
               "All records exist in target."
            );

            return;
         }

         _logger.Information(
            $"Found {total} missing records for {logicalName}. " +
            "Syncing missing records sequentially..."
         );

         // Use a specialized options type to tell the engine to bypass 
         // the local state check during reconciliation.
         var reconciliationOptions = new ReconciliationSyncOptions
         {
            StripMissingDependencies = options.StripMissingDependencies,
            MaxDegreeOfParallelism = 1, // Single-threaded
            PreserveDates = options.PreserveDates
         };

         int processed = 0;
         foreach (var id in missingIds)
         {
            ct.ThrowIfCancellationRequested();

            var record = await source.RetrieveAsync(
               logicalName,
               id,
               null,
               ct
            );

            if (record != null)
            {
               var (success, _) = await engine.SyncRecordAsync(
                  record,
                  reconciliationOptions,
                  ct
               );

               processed++;
               progress?.Report((processed, total, success));
            }
            else
            {
               _logger.Warning(
                  $"Missing record {logicalName}:{id} could not be " +
                  "retrieved from source."
               );

               processed++;
               progress?.Report((processed, total, false));
            }
         }

         _logger.Information($"Reconciliation finished for {logicalName}.");
      }
   }
}
