using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a service that manages temporary 
   /// 'dm_sourcedata' records used for preserving source environment 
   /// audit data (timestamps and user attribution).
   /// </summary>
   public interface ISourceDataService
   {
      /// <summary>
      /// Captures the original audit data from the source entity and 
      /// preserves it by creating a temporary side-car record in the 
      /// target environment.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="sourceEntity">
      /// The entity from the source environment whose audit data needs to be 
      /// preserved.
      /// </param>
      /// <param name="userResolver">
      /// The user resolver used to map source users to the target environment.
      /// </param>
      /// <param name="ct">
      /// A cancellation token that can be used to cancel the operation.
      /// </param>
      /// <returns>
      /// A task that represents the asynchronous operation.
      /// </returns>
      Task CreateSourceDataRecordAsync(
         IDataverseProvider target,
         Entity sourceEntity,
         IUserResolver userResolver,
         CancellationToken ct = default
      );

      /// <summary>
      /// Deletes the temporary side-car record used for audit data 
      /// preservation once the primary entity has been successfully 
      /// synchronized to the target environment.
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
      /// <param name="callerId">
      /// Optional ID of the user to impersonate for this operation.
      /// </param>
      /// <returns>A task that represents the asynchronous deletion.</returns>
      Task DeleteSourceDataRecordAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CancellationToken ct = default,
         Guid? callerId = null
      );
   }
}
