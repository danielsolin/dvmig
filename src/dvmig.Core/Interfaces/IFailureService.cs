using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that logs migration
   /// failures to the target Dataverse environment.
   /// </summary>
   public interface IFailureService
   {
      /// <summary>
      /// Logs a synchronization failure for a specific record.
      /// </summary>
      /// <param name="entity">The entity that failed to sync.</param>
      /// <param name="errorMessage">The error message to record.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task LogFailureToTargetAsync(
         Entity entity,
         string errorMessage,
         CancellationToken ct = default
      );
   }
}
