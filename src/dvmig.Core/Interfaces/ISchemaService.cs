namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for managing schema creation in the target
   /// environment.
   /// </summary>
   public interface ISchemaService
   {
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
          CancellationToken ct = default
      );

      /// <summary>
      /// Removes the 'dm_sourcedate' entity and all its data from 
      /// the target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// A task representing the asynchronous removal operation.
      /// </returns>
      Task DropSchemaAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      );
   }
}
