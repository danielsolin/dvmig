using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for logging migration failures to the target
    /// Dataverse environment.
    /// </summary>
    public interface IFailureLogger
    {
        /// <summary>
        /// Logs a migration failure to the target Dataverse environment by
        /// creating a dm_migrationfailure record.
        /// </summary>
        /// <param name="entity">The entity record that failed to sync.</param>
        /// <param name="errorMessage">
        /// The error message describing why the sync failed.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task LogFailureToTargetAsync(
            Entity entity,
            string errorMessage,
            CancellationToken ct = default);
    }
}
