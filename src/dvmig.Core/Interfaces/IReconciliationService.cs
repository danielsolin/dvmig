using dvmig.Core.Synchronization;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that retrieves and manages 
   /// migration failure records.
   /// </summary>
   public interface IReconciliationService
   {
      /// <summary>
      /// Checks if the failure logging entity is installed on the target.
      /// </summary>
      Task<bool> IsInitializedAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves recorded migration failures from the target.
      /// </summary>
      Task<List<MigrationFailureRecord>> GetFailuresAsync(
          IDataverseProvider target,
          string? entityLogicalName = null,
          CancellationToken ct = default
      );

      /// <summary>
      /// Clears all recorded migration failures from the target.
      /// </summary>
      Task ClearFailuresAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      );

      /// <summary>
      /// Deletes a specific migration failure record.
      /// </summary>
      Task DeleteFailureAsync(
          IDataverseProvider target,
          Guid failureId,
          CancellationToken ct = default
      );

      /// <summary>
      /// Performs a reconciliation for a specific entity, comparing 
      /// counts and attempting to fix discrepancies.
      /// </summary>
      Task PerformReconciliationAsync(
          string logicalName,
          IDataverseProvider source,
          IDataverseProvider target,
          ISyncEngine engine,
          SyncOptions options,
          IProgress<string>? progress = null,
          IProgress<bool>? recordProgress = null,
          CancellationToken ct = default
      );
   }
}
