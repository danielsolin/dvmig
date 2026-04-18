using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dvmig.Providers
{
    public interface IDataverseProvider
    {
        Task<Entity> RetrieveAsync(
            string entityLogicalName,
            Guid id,
            string[] columns = null,
            CancellationToken ct = default);

        Task<EntityMetadata> GetEntityMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default);

        Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default);

        Task UpdateAsync(Entity entity, CancellationToken ct = default);

        Task DeleteAsync(
            string entityLogicalName,
            Guid id,
            CancellationToken ct = default);

        Task AssociateAsync(
            string entityLogicalName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities,
            CancellationToken ct = default);

        Task<EntityCollection> RetrieveMultipleAsync(
            QueryBase query,
            CancellationToken ct = default);

        Guid? CallerId { get; set; }
    }
}
