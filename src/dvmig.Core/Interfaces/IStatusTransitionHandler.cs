using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for handling state and status transitions during
   /// entity synchronization.
   /// </summary>
   public interface IStatusTransitionHandler
   {
      /// <summary>
      /// Handles state and status transitions for entities. This method
      /// strips the state/status codes to allow record creation/update and
      /// then applies them using a SetState request or a subsequent update.
      /// </summary>
      /// <param name="entity">The entity record.</param>
      /// <param name="options">The synchronization configuration.</param>
      /// <param name="progress">The progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="createOrUpdateFunc">
      /// A function that performs the create/update operation for the 
      /// entity.
      /// </param>
      /// <returns>
      /// True if the entity was successfully synchronized and its status
      /// transition applied; otherwise, false.
      /// </returns>
      Task<bool> HandleStatusTransitionAsync(
          Entity entity,
          SyncOptions options,
          CancellationToken ct = default,
          Func<Entity, SyncOptions, CancellationToken,
              Task<(bool Success, string? FailureMessage)>>?
              createOrUpdateFunc = null);
   }
}
