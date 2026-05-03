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
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>True if the environment is initialized.</returns>
      Task<bool> IsInitializedAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves recorded migration failures from the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="entityLogicalName">
      /// Optional filter for a specific entity type.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A list of migration failure records.</returns>
      Task<List<MigrationFailureRecord>> GetFailuresAsync(
         IDataverseProvider target,
         string? entityLogicalName = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears all recorded migration failures from the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      Task ClearFailuresAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Deletes a specific migration failure record by its unique identifier.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="failureId">The ID of the failure record to delete.</param>
      /// <param name="ct">A cancellation token.</param>
      Task DeleteFailureAsync(
         IDataverseProvider target,
         Guid failureId,
         CancellationToken ct = default
      );

      /// <summary>
      /// Performs a reconciliation for a specific entity, comparing
      /// all record IDs and attempting to sync missing ones.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="source">The source Dataverse provider.</param>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="engine">The sync engine instance.</param>
      /// <param name="recordService">The record sync service.</param>
      /// <param name="options">Synchronization options.</param>
      /// <param name="progress">Optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      Task PerformReconciliationAsync(
         string logicalName,
         IDataverseProvider source,
         IDataverseProvider target,
         ISyncEngine engine,
         ISyncRecordService recordService,
         SyncOptions options,
         IProgress<(int Processed, int Total, bool Success)>? progress = null,
         CancellationToken ct = default
      );
   }
}
