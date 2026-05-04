using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IRelationshipService"/> that handles
   /// operations for N:N intersect entities.
   /// </summary>
   public class RelationshipService : IRelationshipService
   {
      private readonly IDataverseProvider _target;
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="RelationshipService"/> class.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="logger">The logger instance.</param>
      public RelationshipService(IDataverseProvider target, ILogger logger)
      {
         _target = target;
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task AssociateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         var request = CreateAssociateRequest(entity);
         if (request == null)
            throw new System.InvalidOperationException(
               "Invalid N:N relationship record."
            );

         await _target.ExecuteAsync(request, ct, callerId);
      }

      /// <inheritdoc />
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
