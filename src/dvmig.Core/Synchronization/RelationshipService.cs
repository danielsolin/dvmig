using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace dvmig.Core.Synchronization
{
   public class RelationshipService : IRelationshipService
   {
      private readonly IDataverseProvider _target;
      private readonly ILogger _logger;

      public RelationshipService(IDataverseProvider target, ILogger logger)
      {
         _target = target;
         _logger = logger;
      }

      public async Task AssociateAsync(Entity entity, CancellationToken ct = default)
      {
         var request = CreateAssociateRequest(entity);
         if (request == null)
            throw new System.InvalidOperationException("Invalid N:N relationship record.");

         await _target.ExecuteAsync(request, ct);
      }

      public AssociateRequest? CreateAssociateRequest(Entity entity)
      {
         var references = entity.Attributes
            .Values.OfType<EntityReference>().ToList();

         if (references.Count < 2)
         {
            _logger.Warning(
               "Intersect entity {Key} does not have " +
               "two EntityReferences.",
               entity.LogicalName
            );

            return null;
         }

         var request = new AssociateRequest
         {
            Target = references[0],
            Relationship = new Relationship(entity.LogicalName),
            RelatedEntities = new EntityReferenceCollection
            {
               references[1]
            }
         };

         return request;
      }
   }
}
