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
      Task AssociateAsync(Entity entity, CancellationToken ct = default);
      AssociateRequest? CreateAssociateRequest(Entity entity);
   }
}
