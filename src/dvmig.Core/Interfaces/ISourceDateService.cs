using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that manages temporary 
   /// 'dm_sourcedate' records used for preserving creation and 
   /// modification dates.
   /// </summary>
   public interface ISourceDateService
   {
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
      Task CreateSourceDateRecordAsync(
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
      Task DeleteSourceDateRecordAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CancellationToken ct = default
      );
   }
}
