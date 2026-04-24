using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Providers
{
    /// <summary>
    /// Interface for a Dataverse provider, abstracting CRUD and metadata 
    /// operations for different versions of the Dataverse/CRM SDK.
    /// </summary>
    public interface IDataverseProvider
    {
        /// <summary>
        /// Retrieves a single entity record by ID.
        /// </summary>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="id">The record ID.</param>
        /// <param name="columns">Optional list of columns to retrieve.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The retrieved entity, or null if not found.</returns>
        Task<Entity?> RetrieveAsync(
            string entityLogicalName,
            Guid id,
            string[]? columns = null,
            CancellationToken ct = default);

        /// <summary>
        /// Retrieves metadata for a specific entity.
        /// </summary>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The entity metadata, or null if retrieval fails.</returns>
        Task<EntityMetadata?> GetEntityMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a new entity record.
        /// </summary>
        /// <param name="entity">The entity to create.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The ID of the newly created record.</returns>
        Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default);

        /// <summary>
        /// Updates an existing entity record.
        /// </summary>
        /// <param name="entity">The entity containing the updates.</param>
        /// <param name="ct">A cancellation token.</param>
        Task UpdateAsync(Entity entity, CancellationToken ct = default);

        /// <summary>
        /// Deletes an entity record.
        /// </summary>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="id">The ID of the record to delete.</param>
        /// <param name="ct">A cancellation token.</param>
        Task DeleteAsync(
            string entityLogicalName,
            Guid id,
            CancellationToken ct = default);

        /// <summary>
        /// Associates records in an N:N relationship.
        /// </summary>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="entityId">The ID of the target record.</param>
        /// <param name="relationship">The relationship definition.</param>
        /// <param name="relatedEntities">The collection of related entities.</param>
        /// <param name="ct">A cancellation token.</param>
        Task AssociateAsync(
            string entityLogicalName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities,
            CancellationToken ct = default);

        /// <summary>
        /// Executes a query and returns a collection of entities.
        /// </summary>
        /// <param name="query">The query to execute.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The resulting entity collection.</returns>
        Task<EntityCollection> RetrieveMultipleAsync(
            QueryBase query,
            CancellationToken ct = default);

        /// <summary>
        /// Executes an organization request.
        /// </summary>
        /// <param name="request">The request to execute.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The organization response.</returns>
        Task<OrganizationResponse> ExecuteAsync(
            OrganizationRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Gets or sets the ID of the user on whose behalf the operations 
        /// are executed.
        /// </summary>
        Guid? CallerId { get; set; }
    }
}
