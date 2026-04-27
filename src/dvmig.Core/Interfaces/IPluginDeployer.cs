using dvmig.Core.Interfaces;

namespace dvmig.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for deploying and registering plugins in the
    /// target environment.
    /// </summary>
    public interface IPluginDeployer
    {
        /// <summary>
        /// Deploys the date preservation plugin assembly to the target 
        /// environment and registers the necessary execution steps.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="pluginAssemblyPath">The local path to the plugin DLL.
        /// </param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous deployment.</returns>
        Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Removes the date preservation plugin assembly and all its 
        /// registered steps from the target environment.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous removal operation.</returns>
        Task RemovePluginAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );
    }
}
