using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Handles operations specific to intersect (N:N) entities, such as 
   /// associating records via AssociateRequest.
   /// </summary>
   public interface IRelationshipService
   {
      /// <summary>
      /// Associates an intersect entity record in the target environment.
      /// </summary>
      /// <param name="entity">The intersect entity to associate.</param>
      /// <param name="ct">A cancellation token.</param>
      Task AssociateAsync(
         Entity entity,
         CancellationToken ct = default
      );

      /// <summary>
      /// Creates an <see cref="AssociateRequest"/> from an intersect entity.
      /// </summary>
      /// <param name="entity">The intersect entity.</param>
      /// <returns>
      /// An associate request, or null if the entity is invalid.
      /// </returns>
      AssociateRequest? CreateAssociateRequest(Entity entity);
   }
}
