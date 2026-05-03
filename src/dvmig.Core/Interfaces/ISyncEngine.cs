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
   }
}
