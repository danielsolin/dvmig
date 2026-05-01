using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for orchestrating error handling strategies during
   /// entity synchronization.
   /// </summary>
   public interface ISyncErrorHandler
   {
      /// <summary>
      /// Orchestrates the error handling strategy for exceptions encountered
      /// during record synchronization. Analyzes the exception type and
      /// applies specific resolution logic (e.g., duplicate handling,
      /// missing dependencies).
      /// </summary>
      /// <param name="ex">The exception that occurred.</param>
      /// <param name="entity">The entity record being synchronized.</param>
      /// <param name="options">The synchronization configuration.</param>
      /// <param name="progress">The progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="updateFunc">
      /// A function that performs an update operation on the entity.
      /// </param>
      /// <param name="statusTransitionFunc">
      /// A function that handles status transitions.
      /// </param>
      /// <param name="resolveMissingDependencyFunc">
      /// A function that resolves missing dependencies.
      /// </param>
      /// <param name="resolveSqlDependencyFunc">
      /// A function that resolves SQL dependencies.
      /// </param>
      /// <param name="stripAttributeFunc">
      /// A function that strips problematic attributes and retries.
      /// </param>
      /// <returns>
      /// A tuple indicating success and any failure message.
      /// </returns>
      Task<(bool Success, string? FailureMessage)> HandleSyncExceptionAsync(
          Exception ex,
          Entity entity,
          SyncOptions options,
          CancellationToken ct = default,
          Func<Entity, CancellationToken, Task>? updateFunc = null,
          Func<Entity, SyncOptions, CancellationToken,
              Task<bool>>? statusTransitionFunc = null,
          Func<Exception, Entity, SyncOptions,
              CancellationToken, Task<bool>>?
              resolveMissingDependencyFunc = null,
          Func<string, Entity, SyncOptions,
              CancellationToken, Task<bool>>?
              resolveSqlDependencyFunc = null,
          Func<Exception, Entity, SyncOptions,
              CancellationToken, Task<bool>>? stripAttributeFunc = null,
          Func<Entity, CancellationToken, Task<Guid?>>?
              findExistingFunc = null);

      /// <summary>
      /// Formats a failure message for logging purposes.
      /// </summary>
      /// <param name="context">
      /// The context where the failure occurred.
      /// </param>
      /// <param name="ex">The exception that occurred.</param>
      /// <returns>A formatted failure message.</returns>
      string FormatFailureMessage(string context, Exception ex);
   }
}
