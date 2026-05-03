using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Handles the synchronization process for individual Dataverse entity records.
   /// </summary>
   public interface ISyncRecordService
   {
      Task SyncRecordAndReportAsync(
         Entity entity,
         SyncOptions options,
         IProgress<bool>? recordProgress,
         CancellationToken ct = default
      );

      Task<(bool Success, string? FailureMessage)> SyncRecordAsync(
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default
      );
   }
}
