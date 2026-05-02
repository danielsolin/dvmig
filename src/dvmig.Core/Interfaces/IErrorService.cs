using Microsoft.Xrm.Sdk;
using dvmig.Core.Synchronization;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that orchestrates
   /// error handling and recovery paths during synchronization.
   /// </summary>
   public interface IErrorService
   {
      /// <summary>
      /// Evaluates an exception and attempts to resolve it using available 
      /// strategies like duplicate detection, dependency resolution, or 
      /// attribute stripping.
      /// </summary>
      /// <param name="ex">The exception that occurred.</param>
      /// <param name="entity">The entity involved in the error.</param>
      /// <param name="options">Synchronization options.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="updateFunc">Optional function for retry updates.</param>
      /// <param name="statusTransitionFunc">
      /// Optional function for status transitions.
      /// </param>
      /// <param name="resolveMissingDependencyFunc">
      /// Optional function for resolving missing lookups.
      /// </param>
      /// <param name="resolveSqlDependencyFunc">
      /// Optional function for resolving SQL conflicts.
      /// </param>
      /// <param name="stripAttributeFunc">
      /// Optional function for stripping problematic attributes.
      /// </param>
      /// <param name="findExistingFunc">
      /// Optional function to find existing records.
      /// </param>
      /// <returns>A tuple indicating success and any failure message.</returns>
      Task<(bool Success, string? FailureMessage)> HandleSyncExceptionAsync(
         Exception ex,
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default,
         Func<Entity, CancellationToken, Task>? updateFunc = null,
         Func<Entity, SyncOptions, CancellationToken, Task<bool>>?
            statusTransitionFunc = null,
         Func<Exception, Entity, SyncOptions, CancellationToken,
            Task<bool>>? resolveMissingDependencyFunc = null,
         Func<string, Entity, SyncOptions, CancellationToken,
            Task<bool>>? resolveSqlDependencyFunc = null,
         Func<Exception, Entity, SyncOptions, CancellationToken,
            Task<bool>>? stripAttributeFunc = null,
         Func<Entity, CancellationToken, Task<Guid?>>? findExistingFunc = null
      );

      /// <summary>
      /// Formats a standardized failure message for logging.
      /// </summary>
      /// <param name="context">The operation context where the error occurred.</param>
      /// <param name="ex">The exception to format.</param>
      /// <returns>A formatted error string.</returns>
      string FormatFailureMessage(string context, Exception ex);
   }
}
