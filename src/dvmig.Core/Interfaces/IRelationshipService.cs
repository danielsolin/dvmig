using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using System.Threading;
using System.Threading.Tasks;

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
