using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for an engine that synchronizes data records 
   /// between a source and target Dataverse environment.
   /// </summary>
   public interface ISyncEngine
   {
      /// <summary>
      /// Synchronizes a collection of entities from the source to 
      /// the target.
      /// </summary>
      /// <param name="entities">The collection of entities to sync.</param>
      /// <param name="options">
      /// The synchronization configuration options.
      /// </param>
      /// <param name="progress">
      /// An optional progress reporter for log messages.
      /// </param>
      /// <param name="recordProgress">
      /// An optional progress reporter for individual record 
      /// success/failure.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// A task representing the asynchronous sync operation.
      /// </returns>
      Task SyncAsync(
          IEnumerable<Entity> entities,
          SyncOptions options,
          IProgress<string>? progress = null,
          IProgress<bool>? recordProgress = null,
          CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record from the source to the target.
      /// </summary>
      /// <param name="entity">The entity record to synchronize.</param>
      /// <param name="options">
      /// The synchronization configuration options.
      /// </param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// True if the synchronization was successful; otherwise, false.
      /// </returns>
      Task<(bool Success, string? FailureMessage)> SyncRecordAsync(
          Entity entity,
          SyncOptions options,
          IProgress<string>? progress = null,
          CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves a set of valid attributes for extraction, excluding 
      /// logical or non-readable columns to optimize query performance.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// A configured ColumnSet containing valid attributes.
      /// </returns>
      Task<Microsoft.Xrm.Sdk.Query.ColumnSet> GetValidColumnsAsync(
          string logicalName,
          CancellationToken ct = default
      );

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
      /// <param name="progress">Progress reporter for log messages.</param>
      /// <param name="recordProgress">
      /// Progress reporter for success/failure.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the operation.</returns>
      Task SyncEntityAsync(
          string logicalName,
          SyncOptions options,
          Microsoft.Xrm.Sdk.Query.QueryExpression? query = null,
          IProgress<string>? progress = null,
          IProgress<bool>? recordProgress = null,
          CancellationToken ct = default
      );

      /// <summary>
      /// Initializes the sync engine for a specific entity type, ensuring 
      /// the state tracker is ready for the current job.
      /// </summary>
      /// <param name="logicalName">The logical name of the entity.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task InitializeEntitySyncAsync(string logicalName);
   }
}
