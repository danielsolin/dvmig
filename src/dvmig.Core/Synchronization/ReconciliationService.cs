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
          IProgress<string>? progress = null,
          IProgress<bool>? recordProgress = null,
          CancellationToken ct = default
      )
      {
         progress?.Report($"Reconciling {logicalName}...");

         var sourceCount = await source.GetRecordCountAsync(logicalName, ct);
         var targetCount = await target.GetRecordCountAsync(logicalName, ct);

         progress?.Report(
             $"Counts: Source={sourceCount}, Target={targetCount}"
         );

         if (sourceCount <= targetCount)
         {
            progress?.Report("Counts match or target has more records. " +
                             "No action needed.");

            return;
         }

         var diff = sourceCount - targetCount;
         progress?.Report($"Discrepancy found: {diff} records missing.");

         // 1. Try to re-sync logged failures
         var failures = await GetFailuresAsync(target, logicalName, ct);
         if (failures.Count > 0)
         {
            progress?.Report($"Found {failures.Count} logged failures. " +
                             "Attempting re-sync...");

            int fixedCount = 0;
            foreach (var failure in failures)
            {
               ct.ThrowIfCancellationRequested();

               if (!Guid.TryParse(failure.SourceId, out var sourceId))
                  continue;

               var sourceRecord = await source.RetrieveAsync(
                   logicalName,
                   sourceId,
                   null,
                   ct
               );

               if (sourceRecord == null)
               {
                  progress?.Report(
                      $"Source record {sourceId} not found. " +
                      "Skipping failure re-sync."
                  );

                  recordProgress?.Report(false);
                  continue;
               }

               var (success, _) = await engine.SyncRecordAsync(
                   sourceRecord,
                   options,
                   progress,
                   ct
               );

               if (success)
               {
                  await DeleteFailureAsync(target, failure.Id, ct);
                  fixedCount++;
               }

               recordProgress?.Report(success);
            }

            progress?.Report($"Fixed {fixedCount}/{failures.Count} " +
                             "logged failures.");
         }

         // 2. Check counts again
         targetCount = await target.GetRecordCountAsync(logicalName, ct);
         if (sourceCount > targetCount)
         {
            progress?.Report(
                $"Still missing {sourceCount - targetCount} records. " +
                "Performing a full sync pass using state log..."
            );

            await engine.SyncEntityAsync(
                logicalName,
                options,
                null,
                progress,
                recordProgress,
                ct
            );

            targetCount = await target.GetRecordCountAsync(logicalName, ct);
            progress?.Report(
                $"Final counts: Source={sourceCount}, Target={targetCount}"
            );
         }
         else
            progress?.Report("All records successfully reconciled.");
      }
   }
}
