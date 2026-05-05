using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Polly.Retry;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Consolidated service for handling synchronization resilience, 
   /// including error handling, retries, dependency resolution, 
   /// and status transitions.
   /// </summary>
   public interface ISyncResilienceService
   {
      /// <summary>
      /// Evaluates an exception and attempts to resolve it using available 
      /// strategies like duplicate detection, dependency resolution, or 
      /// attribute stripping.
      /// </summary>
      Task<(bool Success, string? FailureMessage)> HandleSyncExceptionAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId = null,
         Guid? modifiedById = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Formats a standardized failure message for logging.
      /// </summary>
      string FormatFailureMessage(string context, Exception ex);

      /// <summary>
      /// Manages state and status code transitions for an entity.
      /// </summary>
      Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         CancellationToken ct = default,
         Guid? callerId = null
      );

      /// <summary>
      /// Attempts to resolve missing Dataverse or SQL dependencies.
      /// </summary>
      Task<bool> ResolveDependencyAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId = null,
         Guid? modifiedById = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Determines if an exception represents a transient error.
      /// </summary>
      bool IsTransientError(Exception ex);

      /// <summary>
      /// Creates a retry policy configured with the service's rules.
      /// </summary>
      AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5);

      /// <summary>
      /// Sets the sync engine reference needed for record-level recovery.
      /// </summary>
      void SetEngine(ISyncEngine engine);
   }
}
