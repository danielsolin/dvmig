using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Handles the synchronization process for individual Dataverse entity records.
   /// </summary>
   public interface ISyncRecordService
   {
      /// <summary>
      /// Synchronizes a single entity record and reports success or failure 
      /// via the progress reporter.
      /// </summary>
      /// <param name="entity">The entity to synchronize.</param>
      /// <param name="options">Synchronization options.</param>
      /// <param name="recordProgress">Optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CancellationToken ct = default
      );

      /// <summary>
      /// Synchronizes a single entity record to the target environment.
      /// </summary>
      /// <param name="entity">The entity to synchronize.</param>
      /// <param name="options">Synchronization options.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A tuple indicating success and any failure message.</returns>
      Task<(bool Success, string? FailureMessage)> SyncRecordAsync(
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default
      );
   }
}
