using dvmig.Providers;

namespace dvmig.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for validating target environment readiness.
    /// </summary>
    public interface IEnvironmentValidator
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
    }
}
