using dvmig.Core.Interfaces;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that manages the lifecycle 
   /// and readiness of Dataverse environments for migration.
   /// </summary>
   public interface IEnvironmentService
   {
      /// <summary>
      /// Validates that the target environment is ready for migration.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>True if the environment is valid.</returns>
      Task<bool> ValidateTargetEnvironmentAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Installs all required dvmig components on the target 
      /// environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      Task InstallComponentsAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );

      /// <summary>
      /// Uninstalls all dvmig components from the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="ct">A cancellation token.</param>
      Task UninstallComponentsAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      );
   }
}
