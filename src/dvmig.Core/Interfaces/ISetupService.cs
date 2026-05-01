using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that prepares a target Dataverse 
   /// environment for migration. This includes verifying readiness, creating 
   /// required schema components, and deploying necessary plugins.
   /// </summary>
   public interface ISetupService
   {
      /// <summary>
      /// Deploys the date preservation plugin assembly to the target 
      /// environment and registers the necessary execution steps.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous deployment.</returns>
      Task DeployPluginAsync(
          IDataverseProvider target,
          IProgress<string>? progress = null,
          CancellationToken ct = default
      );

      /// <summary>
      /// Removes all migration-specific components (schema and plugins) 
      /// from the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// A task representing the asynchronous cleanup operation.
      /// </returns>
      Task CleanEnvironmentAsync(
          IDataverseProvider target,
          IProgress<string>? progress = null,
          CancellationToken ct = default
      );
   }
}
