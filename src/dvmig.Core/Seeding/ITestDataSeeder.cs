using dvmig.Providers;

namespace dvmig.Core.Seeding
{
    /// <summary>
    /// Defines the contract for a service that generates and inserts test 
    /// data into a Dataverse environment.
    /// </summary>
    public interface ITestDataSeeder
    {
        /// <summary>
        /// Generates and inserts a specified number of test records into 
        /// the environment.
        /// </summary>
        /// <param name="provider">The Dataverse provider to use.</param>
        /// <param name="count">The number of records to generate per entity.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous seeding operation.</returns>
        Task SeedTestDataAsync(
            IDataverseProvider provider,
            int count,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Deletes all Account and Contact records from the environment.
        /// [DANGEROUS] This should only be used in isolated test environments.
        /// </summary>
        /// <param name="provider">The Dataverse provider to use.</param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous cleaning operation.</returns>
        Task CleanTestDataAsync(
            IDataverseProvider provider,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );

        /// <summary>
        /// Gets the total number of records for a specific entity in the 
        /// environment.
        /// </summary>
        /// <param name="provider">The Dataverse provider to use.</param>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The total number of records.</returns>
        Task<long> GetRecordCountAsync(
            IDataverseProvider provider,
            string logicalName,
            CancellationToken ct = default
        );
    }
}
