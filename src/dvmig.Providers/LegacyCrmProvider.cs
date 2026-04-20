using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

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
                    $"Legacy CRM connection failed: {_client.LastCrmError}");
            }
        }

        public Guid? CallerId
        {
            get => _client.CallerId;
            set => _client.CallerId = value ?? Guid.Empty;
        }

        public Task<Entity?> RetrieveAsync(
            string entityLogicalName,
            Guid id,
            string[]? columns = null,
            CancellationToken ct = default)
        {
            var columnSet = columns == null ? new ColumnSet(true) : 
                new ColumnSet(columns);
            
            return Task.FromResult<Entity?>(_client.Retrieve(
                entityLogicalName, id, columnSet));
        }

        public Task<EntityMetadata?> GetEntityMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default)
        {
            var response = _client.Execute(
                new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Attributes
            }) as Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse;

            return Task.FromResult(response?.EntityMetadata);
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default)
        {
            return Task.FromResult(_client.Create(entity));
        }

        public Task UpdateAsync(Entity entity, CancellationToken ct = default)
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
                entityLogicalName, entityId, relationship, relatedEntities);
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
