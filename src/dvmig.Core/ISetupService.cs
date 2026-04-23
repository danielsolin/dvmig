using dvmig.Providers;

namespace dvmig.Core
{
    /// <summary>
    /// Defines the contract for a service that prepares a target Dataverse 
    /// environment for migration. This includes verifying readiness, creating 
    /// required schema components, and deploying necessary plugins.
    /// </summary>
    public interface ISetupService
    {
        /// <summary>
        /// Checks whether the target environment is fully configured for 
        /// migration, including the presence of the 'dm_sourcedate' entity, 
        /// the plugin assembly, and the required plugin steps.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>True if the environment is ready; otherwise, false.</returns>
        Task<bool> IsEnvironmentReadyAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        );

        /// <summary>
        /// Creates the 'dm_sourcedate' entity schema and its required 
        /// attributes in the target environment if they do not already exist.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CreateSchemaAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Deploys the date preservation plugin assembly to the target 
        /// environment and registers the necessary execution steps.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="pluginAssemblyPath">The local path to the plugin DLL.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous deployment.</returns>
        Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );
    }
}
