using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
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
                    $"Dataverse connection failed: {_client.LastError}"
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

        public async Task<Entity?> RetrieveAsync(
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

                return await _client.RetrieveAsync(
                    entityLogicalName,
                    id,
                    columnSet,
                    ct
                );
            }
            catch (FaultException ex)
            {
                // 0x80040217 = Object does not exist
                if (ex.Message.Contains("80040217") ||
                    ex.Message.Contains("Does Not Exist"))
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<EntityMetadata?> GetEntityMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default)
        {
            try
            {
                var response = await _client.ExecuteAsync(
                    new RetrieveEntityRequest
                    {
                        LogicalName = entityLogicalName,
                        EntityFilters = EntityFilters.Attributes
                    },
                    ct
                ) as RetrieveEntityResponse;

                return response?.EntityMetadata;
            }
            catch
            {
                // If entity doesn't exist, RetrieveEntityRequest throws.
                // We return null to indicate missing metadata.
                return null;
            }
        }

        public async Task<Guid> CreateAsync(
            Entity entity,
            CancellationToken ct = default)
        {
            return await _client.CreateAsync(entity, ct);
        }

        public async Task UpdateAsync(
            Entity entity,
            CancellationToken ct = default)
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
                entityLogicalName,
                entityId,
                relationship,
                relatedEntities,
                ct
            );
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
