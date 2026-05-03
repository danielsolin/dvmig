namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that wipes data from a Dataverse 
   /// environment.
   /// </summary>
   public interface IWipeDataService
   {
      /// <summary>
      /// Wipes records of specified entities from the environment.
      /// </summary>
      /// <param name="provider">The Dataverse provider.</param>
      /// <param name="entities">Optional list of entities to clean.</param>
      /// <param name="progress">
      /// Optional progress reporter for remaining count.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A task representing the asynchronous operation.</returns>
      Task WipeEntitiesAsync(
         IDataverseProvider provider,
         List<string>? entities = null,
         IProgress<long>? progress = null,
         CancellationToken ct = default
      );
   }
}
