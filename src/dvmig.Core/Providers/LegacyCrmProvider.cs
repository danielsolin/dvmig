using dvmig.Core.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the legacy
   /// <see cref="CrmServiceClient"/> from the XrmTooling.Connector SDK.
   /// Required for OnPrem (AD/IFD) authentication support.
   /// </summary>
   public class LegacyCrmProvider : IDataverseProvider, IDisposable
   {
      private readonly CrmServiceClient _client;

      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="LegacyCrmProvider"/> class.
      /// </summary>
      /// <param name="connectionString">
      /// The connection string to the legacy CRM environment.
      /// </param>
      public LegacyCrmProvider(string connectionString)
      {
         ConnectionString = connectionString;
         _client = new CrmServiceClient(connectionString);

         if (!_client.IsReady)
            throw new Exception(
               $"Legacy CRM connection failed: {_client.LastCrmError}"
            );
      }

      /// <inheritdoc />
      public Guid? CallerId
      {
         get
         {
            return _client.CallerId;
         }
         set
         {
            _client.CallerId = value ?? Guid.Empty;
         }
      }

      /// <inheritdoc />
      public Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      )
      {
         try
         {
            var columnSet = columns == null
               ? new ColumnSet(true)
               : new ColumnSet(columns);

            return Task.FromResult<Entity?>(
               _client.Retrieve(
                  entityLogicalName,
                  id,
                  columnSet
               )
            );
         }
         catch (Exception ex)
         {
            if (ex.IsNotFoundException())
               return Task.FromResult<Entity?>(null);

            throw;
         }
      }

      /// <inheritdoc />
      public Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         var response = _client.Execute(
            new RetrieveEntityRequest
            {
               LogicalName = entityLogicalName,
               EntityFilters = EntityFilters.Attributes
            }
         ) as RetrieveEntityResponse;

         return Task.FromResult(response?.EntityMetadata);
      }

      /// <inheritdoc />
      public Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         if (callerId.HasValue && callerId.Value != Guid.Empty)
         {
            using (var clonedClient = _client.Clone())
            {
               clonedClient.CallerId = callerId.Value;

               return Task.FromResult(clonedClient.Create(entity));
            }
         }

         return Task.FromResult(_client.Create(entity));
      }

      /// <inheritdoc />
      public Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         if (callerId.HasValue && callerId.Value != Guid.Empty)
         {
            using (var clonedClient = _client.Clone())
            {
               clonedClient.CallerId = callerId.Value;
               clonedClient.Update(entity);

               return Task.CompletedTask;
            }
         }

         _client.Update(entity);

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         if (callerId.HasValue && callerId.Value != Guid.Empty)
         {
            using (var clonedClient = _client.Clone())
            {
               clonedClient.CallerId = callerId.Value;
               clonedClient.Delete(entityLogicalName, id);

               return Task.CompletedTask;
            }
         }

         _client.Delete(entityLogicalName, id);

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         if (callerId.HasValue && callerId.Value != Guid.Empty)
         {
            using (var clonedClient = _client.Clone())
            {
               clonedClient.CallerId = callerId.Value;

               clonedClient.Associate(
                  entityLogicalName,
                  entityId,
                  relationship,
                  relatedEntities
               );

               return Task.CompletedTask;
            }
         }

         _client.Associate(
            entityLogicalName,
            entityId,
            relationship,
            relatedEntities
         );

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         if (callerId.HasValue && callerId.Value != Guid.Empty)
         {
            using (var clonedClient = _client.Clone())
            {
               clonedClient.CallerId = callerId.Value;

               return Task.FromResult(clonedClient.RetrieveMultiple(query));
            }
         }

         return Task.FromResult(_client.RetrieveMultiple(query));
      }

      /// <inheritdoc />
      public Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         if (callerId.HasValue && callerId.Value != Guid.Empty)
         {
            using (var clonedClient = _client.Clone())
            {
               clonedClient.CallerId = callerId.Value;

               return Task.FromResult(clonedClient.Execute(request));
            }
         }

         return Task.FromResult(_client.Execute(request));
      }

      /// <summary>
      /// Disposes the underlying CRM service client.
      /// </summary>
      public void Dispose()
      {
         _client.Dispose();
      }
   }
}
