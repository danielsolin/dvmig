using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace dvmig.Providers
{
    public class LegacyCrmProvider : IDataverseProvider, IDisposable
    {
        private readonly CrmServiceClient _client;

        public LegacyCrmProvider(string connectionString)
        {
            _client = new CrmServiceClient(connectionString);
            if (!_client.IsReady)
            {
                throw new Exception(
                    $"Legacy CRM connection failed: {_client.LastCrmError}"
                );
            }
        }

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

        public Task<Entity?> RetrieveAsync(
            string entityLogicalName,
            Guid id,
            string[]? columns = null,
            CancellationToken ct = default)
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
            catch (FaultException ex)
            {
                if (ex.Message.Contains("80040217") || 
                    ex.Message.Contains("Does Not Exist"))
                {
                    return Task.FromResult<Entity?>(null);
                }

                throw;
            }
        }

        public Task<EntityMetadata?> GetEntityMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default)
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

        public Task<Guid> CreateAsync(
            Entity entity,
            CancellationToken ct = default)
        {
            return Task.FromResult(_client.Create(entity));
        }

        public Task UpdateAsync(
            Entity entity,
            CancellationToken ct = default)
        {
            _client.Update(entity);

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string entityLogicalName,
            Guid id,
            CancellationToken ct = default)
        {
            _client.Delete(entityLogicalName, id);

            return Task.CompletedTask;
        }

        public Task AssociateAsync(
            string entityLogicalName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities,
            CancellationToken ct = default)
        {
            _client.Associate(
                entityLogicalName,
                entityId,
                relationship,
                relatedEntities
            );

            return Task.CompletedTask;
        }

        public Task<EntityCollection> RetrieveMultipleAsync(
            QueryBase query,
            CancellationToken ct = default)
        {
            return Task.FromResult(_client.RetrieveMultiple(query));
        }

        public Task<OrganizationResponse> ExecuteAsync(
            OrganizationRequest request,
            CancellationToken ct = default)
        {
            return Task.FromResult(_client.Execute(request));
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}