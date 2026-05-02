using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that handles state and 
   /// status transitions during entity synchronization.
   /// </summary>
   public interface IStatusService
   {
      /// <summary>
      /// Manages the state and status code transitions for an entity, 
      /// handling scenarios where direct updates are restricted by 
      /// Dataverse business logic.
      /// </summary>
      /// <param name="entity">The entity to update.</param>
      /// <param name="options">Synchronization options.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="createOrUpdateFunc">
      /// Optional custom function for the base create/update.
      /// </param>
      /// <returns>True if the transition was successful.</returns>
      Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         dvmig.Core.Synchronization.SyncOptions options,
         CancellationToken ct = default,
         Func<Entity, dvmig.Core.Synchronization.SyncOptions, 
            CancellationToken, Task<(bool Success, string? FailureMessage)>>? 
            createOrUpdateFunc = null
      );
   }
}
