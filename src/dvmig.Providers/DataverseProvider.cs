using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Providers
{
    public class DataverseProvider : IDataverseProvider, IDisposable
    {
        private readonly ServiceClient _client;

        public DataverseProvider(string connectionString)
        {
            _client = new ServiceClient(connectionString);
            if (!_client.IsReady)
            {
                throw new Exception(
                    $"Dataverse connection failed: {_client.LastError}");
            }
        }

        public Guid? CallerId
        {
            get => _client.CallerId;
            set => _client.CallerId = value ?? Guid.Empty;
        }

        public async Task<Entity> RetrieveAsync(
            string entityLogicalName,
            Guid id,
            string[] columns = null,
            CancellationToken ct = default)
        {
            var columnSet = columns == null ? new ColumnSet(true) : 
                new ColumnSet(columns);
            return await _client.RetrieveAsync(
                entityLogicalName, id, columnSet, ct);
        }

        public async Task<EntityMetadata> GetEntityMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default)
        {
            var response = await _client.ExecuteAsync(
                new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Attributes
            }, ct) as Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse;

            return response?.EntityMetadata;
        }

        public async Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default)
        {
            return await _client.CreateAsync(entity, ct);
        }

        public async Task UpdateAsync(Entity entity, CancellationToken ct = default)
        {
            await _client.UpdateAsync(entity, ct);
        }

        public async Task DeleteAsync(
            string entityLogicalName,
            Guid id,
            CancellationToken ct = default)
        {
            await _client.DeleteAsync(entityLogicalName, id, ct);
        }

        public async Task AssociateAsync(
            string entityLogicalName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities,
            CancellationToken ct = default)
        {
            await _client.AssociateAsync(
                entityLogicalName, entityId, relationship, relatedEntities, ct);
        }

        public async Task<EntityCollection> RetrieveMultipleAsync(
            QueryBase query,
            CancellationToken ct = default)
        {
            return await _client.RetrieveMultipleAsync(query, ct);
        }

        public async Task<OrganizationResponse> ExecuteAsync(
            OrganizationRequest request,
            CancellationToken ct = default)
        {
            return await _client.ExecuteAsync(request, ct);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
