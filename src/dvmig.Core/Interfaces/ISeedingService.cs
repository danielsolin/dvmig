namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that seeds and cleans test data in
   /// a Dataverse environment.
   /// </summary>
   public interface ISeedingService
   {
      /// <summary>
      /// Seeds sample data into the specified environment.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="recordCount">The number of records per entity.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task SeedSampleDataAsync(
         IDataverseProvider provider,
         int recordCount = 50,
         CancellationToken ct = default
      );

      /// <summary>
      /// Cleans test data from the specified environment.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="entities">Optional list of entities to clean.</param>
      /// <param name="progress">Optional progress reporter for remaining count.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task CleanTestDataAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         IProgress<long>? progress = null,
         CancellationToken ct = default
      );
   }
}
