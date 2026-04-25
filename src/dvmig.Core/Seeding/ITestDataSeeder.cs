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
    }
}
