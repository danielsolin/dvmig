using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for an engine that synchronizes data records 
   /// between a source and target Dataverse environment.
   /// </summary>
   public interface ISyncEngine
   {
      /// <summary>
      /// Synchronizes all records of a specific entity type from the 
      /// source to the target, handling pagination automatically.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="options">The sync options.</param>
      /// <param name="query">
      /// An optional query to filter the records to be synchronized. 
      /// If null, all records are fetched.
      /// </param>
      /// <param name="recordProgress">
      /// Progress reporter for success/failure.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the operation.</returns>
      Task SyncAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Initializes the sync engine for a specific entity type, ensuring 
      /// the state tracker is ready for the current job.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task InitializeEntitySyncAsync(
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record and reports success or failure 
      /// via the progress reporter.
      /// </summary>
      Task SyncRecordAndReportAsync(
         Microsoft.Xrm.Sdk.Entity entity,
         Synchronization.SyncOptions options,
         IProgress<bool>? recordProgress,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record to the target environment.
      /// </summary>
      Task<(bool Success, string? FailureMessage)> SyncRecordAsync(
         Microsoft.Xrm.Sdk.Entity entity,
         Synchronization.SyncOptions options,
         CancellationToken ct = default
      );

      /// <summary>
      /// Finds an existing record on the target environment that matches 
      /// the source record.
      /// </summary>
      Task<Guid?> FindExistingOnTargetAsync(
         Microsoft.Xrm.Sdk.Entity entity,
         CancellationToken ct = default
      );
   }
}
