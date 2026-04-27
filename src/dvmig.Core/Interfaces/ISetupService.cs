using Microsoft.Xrm.Sdk;
using dvmig.Core.Providers;

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
        /// Checks whether the target environment is fully configured for 
        /// migration, including the presence of the 'dm_sourcedate' entity, 
        /// the plugin assembly, and the required plugin steps.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the environment is ready; otherwise, false.
        /// </returns>
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
        /// <returns>A task representing the asynchronous cleanup operation.</returns>
        Task CleanEnvironmentAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Captures the original creation and modification dates from the 
        /// source entity and preserves them by creating a temporary 
        /// side-car record in the target environment.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="sourceEntity">
        /// The entity from the source environment whose dates need to be 
        /// preserved.
        /// </param>
        /// <param name="ct">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// </returns>
        Task PreserveDatesAsync(
            IDataverseProvider target,
            Entity sourceEntity,
            CancellationToken ct = default
        );

        /// <summary>
        /// Deletes the temporary side-car record used for date preservation 
        /// once the primary entity has been successfully synchronized to 
        /// the target environment.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="logicalName">
        /// The logical name of the primary entity.
        /// </param>
        /// <param name="entityId">
        /// The unique identifier of the primary entity.
        /// </param>
        /// <param name="ct">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>A task that represents the asynchronous deletion.</returns>
        Task DeleteSourceDateAsync(
            IDataverseProvider target,
            string logicalName,
            Guid entityId,
            CancellationToken ct = default
        );
    }
}
