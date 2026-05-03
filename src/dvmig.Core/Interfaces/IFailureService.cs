using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that logs and manages migration
   /// failures in the target Dataverse environment.
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

      /// <summary>
      /// Checks if the failure logging entity is installed on the target.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>True if the environment is initialized.</returns>
      Task<bool> IsInitializedAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Retrieves recorded migration failures from the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="entityLogicalName">
      /// Optional filter for a specific entity type.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A list of migration failure records.</returns>
      Task<List<MigrationFailureRecord>> GetFailuresAsync(
         IDataverseProvider target,
         string? entityLogicalName = null,
         CancellationToken ct = default
      );

      /// <summary>
      /// Clears all recorded migration failures from the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      Task ClearFailuresAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Deletes a specific migration failure record by its unique identifier.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="failureId">The ID of the failure record to delete.</param>
      /// <param name="ct">A cancellation token.</param>
      Task DeleteFailureAsync(
         IDataverseProvider target,
         Guid failureId,
         CancellationToken ct = default
      );
   }
}
