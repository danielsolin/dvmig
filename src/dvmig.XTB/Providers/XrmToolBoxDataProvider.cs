using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;

using dvmig.Core.Interfaces;

namespace dvmig.XTB.Providers
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> that wraps an 
   /// XrmToolBox <see cref="IOrganizationService"/>.
   /// </summary>
   public class XrmToolBoxDataProvider : IDataverseProvider
   {
      private readonly IOrganizationService _service;
      
      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="XrmToolBoxDataProvider"/> class.
      /// </summary>
      /// <param name="service">The organization service.</param>
      /// <param name="connectionName">A  name for the connection.</param>
      public XrmToolBoxDataProvider(
         IOrganizationService service, 
         string connectionName
      )
      {
         _service = service;
         ConnectionString = connectionName;
      }

      /// <inheritdoc />
      public async Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() =>
         {
            try
            {
               var columnSet = columns == null
                  ? new ColumnSet(true)
                  : new ColumnSet(columns);

               return _service.Retrieve(entityLogicalName, id, columnSet);
            }
            catch
            {
               return null;
            }
         }, ct);
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() =>
         {
            try
            {
               var request = new RetrieveEntityRequest
               {
                  LogicalName = entityLogicalName,
                  EntityFilters = EntityFilters.Attributes
               };

               var response = (RetrieveEntityResponse)
                  _service.Execute(request);

               return response.EntityMetadata;
            }
            catch
            {
               return null;
            }
         }, ct);
      }

      /// <inheritdoc />
      public async Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         // Note: IOrganizationService in XTB might not easily support 
         // callerId impersonation without creating a new service proxy, 
         // which is complex in XTB context. For now, we use the base service.
         return await Task.Run(() => _service.Create(entity), ct);
      }

      /// <inheritdoc />
      public async Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         await Task.Run(() => _service.Update(entity), ct);
      }

      /// <inheritdoc />
      public async Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      )
      {
         await Task.Run(() => _service.Delete(entityLogicalName, id), ct);
      }

      /// <inheritdoc />
      public async Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default
      )
      {
         await Task.Run(() => 
            _service.Associate(
               entityLogicalName, 
               entityId, 
               relationship, 
               relatedEntities
            ), 
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() => _service.RetrieveMultiple(query), ct);
      }

      /// <inheritdoc />
      public async Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await Task.Run(() => _service.Execute(request), ct);
      }
   }
}
