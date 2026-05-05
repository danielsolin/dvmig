using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for an engine that synchronizes data records 
   /// between a source and target Dataverse environment.
   /// </summary>
   public interface ISyncEngine
   {
      #region Core Sync Operations

      /// <summary>
      /// Synchronizes all records of a specific entity type from the 
      /// source to the target, handling pagination automatically.
      /// </summary>
      Task SyncAsync(
         string logicalName,
         SyncOptions options,
         QueryExpression? query = null,
         IProgress<bool>? recordProgress = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Initializes the sync engine for a specific entity type.
      /// </summary>
      Task InitializeEntitySyncAsync(
         string logicalName,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record and reports success or failure 
      /// via the progress reporter.
      /// </summary>
      Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record to the target environment.
      /// </summary>
      Task<(bool Success, string? FailureMessage)> SyncRecordAsync(
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default
      );

      /// <summary>
      /// Finds an existing record on the target environment.
      /// </summary>
      Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         CancellationToken ct = default
      );

      #endregion

      #region Failure Management

      /// <summary>
      /// Logs a migration failure directly to the target environment.
      /// </summary>
      Task LogFailureToTargetAsync(
         Entity entity,
         string errorMessage,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves recorded migration failures from the target environment.
      /// </summary>
      Task<List<MigrationFailureRecord>> GetFailuresAsync(
         IDataverseProvider target,
         string? entityLogicalName = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears all recorded failures on the target environment.
      /// </summary>
      Task ClearFailuresAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Checks if the target environment supports migration failure logging.
      /// </summary>
      Task<bool> IsFailureLoggingInitializedAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      #endregion
   }
}
