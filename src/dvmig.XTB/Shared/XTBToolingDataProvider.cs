using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

using dvmig.Core.Interfaces;
using dvmig.Core.Providers;

namespace dvmig.XTB.Shared
{
   internal sealed class XTBToolingDataProvider : IDataverseProvider
   {
      private readonly CrmServiceClient _client;

      public XTBToolingDataProvider(
         CrmServiceClient client,
         string connectionName,
         bool isLegacy = false
      )
      {
         _client = client;
         ConnectionString = connectionName;
         IsLegacy = isLegacy;
      }

      public string ConnectionString { get; }

      public bool IsLegacy { get; }

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

               return Execute(client =>
                  client.Retrieve(entityLogicalName, id, columnSet)
               );
            }
            catch (Exception ex)
            {
               if (ex.IsNotFoundException())
                  return null;

               throw;
            }
         }, ct);
      }

      public async Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() =>
         {
            try
            {
               var response = Execute(client =>
                  client.Execute(
                     new RetrieveEntityRequest
                     {
                        LogicalName = entityLogicalName,
                        EntityFilters = EntityFilters.Attributes
                     }
                  )
               ) as RetrieveEntityResponse;

               return response?.EntityMetadata;
            }
            catch
            {
               return null;
            }
         }, ct);
      }

      public async Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await Task.Run(() =>
            Execute(client => client.Create(entity), callerId),
            ct
         );
      }

      public async Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         await Task.Run(() =>
            ExecuteVoid(client =>
            {
               client.Update(entity);
            },
            callerId),
            ct
         );
      }

      public async Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      )
      {
         await Task.Run(() => 
            ExecuteVoid(client =>
            {
               client.Delete(entityLogicalName, id);
            }),
            ct
         );
      }

      public async Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default
      )
      {
         await Task.Run(() =>
            ExecuteVoid(client =>
            {
               client.Associate(
                  entityLogicalName,
                  entityId,
                  relationship,
                  relatedEntities
               );
            }),
            ct
         );
      }

      public async Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() =>
            Execute(client => client.RetrieveMultiple(query)),
            ct
         );
      }

      public async Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await Task.Run(() =>
            Execute(client => client.Execute(request), callerId),
            ct
         );
      }

      private void ExecuteVoid(
         Action<CrmServiceClient> action,
         Guid? callerId = null
      )
      {
         Execute(client =>
         {
            action(client);
            return true;
         },
         callerId);
      }

      private T Execute<T>(
         Func<CrmServiceClient, T> action,
         Guid? callerId = null
      )
      {
         // Sharing XTB's client serializes concurrent sync operations.
         using var clonedClient = _client.Clone();

         if (callerId.HasValue && callerId.Value != Guid.Empty)
            clonedClient.CallerId = callerId.Value;

         return action(clonedClient);
      }
   }
}
