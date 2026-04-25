using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
    public partial class SyncEngine
    {
        /// <summary>
        /// Prepares a source entity for the target environment by mapping 
        /// users, resolving lookups through the ID cache, and stripping 
        /// forbidden or invalid attributes.
        /// </summary>
        /// <param name="entity">The source entity record.</param>
        /// <param name="metadata">The entity metadata from the target.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A prepared entity record ready for the target.</returns>
        private async Task<Entity> PrepareEntityForTargetAsync(
            Entity entity,
            EntityMetadata metadata,
            SyncOptions options,
            CancellationToken ct)
        {
            var targetEntity = new Entity(entity.LogicalName, entity.Id);

            foreach (var attribute in entity.Attributes)
            {
                if (IsForbiddenAttribute(attribute.Key))
                {
                    continue;
                }

                var attrMetadata = metadata.Attributes
                    .FirstOrDefault(a => a.LogicalName == attribute.Key);

                if (attrMetadata != null && attrMetadata.IsValidForCreate == false &&
                    attrMetadata.IsValidForUpdate == false)
                {
                    continue;
                }

                var value = attribute.Value;

                // Handle EntityReference mapping
                if (value is EntityReference er)
                {
                    if (IsUserAttribute(attribute.Key))
                    {
                        value = await _userMapper.MapUserAsync(er, ct);
                    }
                    else if (_idMappingCache.TryGetValue(
                        $"{er.LogicalName}:{er.Id}",
                        out var mappedId))
                    {
                        value = new EntityReference(er.LogicalName, mappedId);
                    }
                }

                targetEntity[attribute.Key] = value;
            }

            return targetEntity;
        }

        /// <summary>
        /// Attempts to find a record on the target environment that matches 
        /// the source record based on its primary name or business key.
        /// </summary>
        /// <param name="entity">The source entity record.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// The ID of the matching record on the target, or null if not found.
        /// </returns>
        private async Task<Guid?> FindExistingOnTargetAsync(
            Entity entity,
            CancellationToken ct)
        {
            var metadata = await GetMetadataAsync(entity.LogicalName, ct);
            if (metadata == null) return null;

            var primaryNameAttr = metadata.PrimaryNameAttribute;
            if (string.IsNullOrEmpty(primaryNameAttr) ||
                !entity.Contains(primaryNameAttr))
            {
                return null;
            }

            var query = new QueryByAttribute(entity.LogicalName)
            {
                ColumnSet = new ColumnSet(metadata.PrimaryIdAttribute)
            };
            query.AddAttributeValue(primaryNameAttr, entity[primaryNameAttr]);

            var results = await _target.RetrieveMultipleAsync(query, ct);
            return results.Entities.FirstOrDefault()?.Id;
        }

        /// <summary>
        /// Retrieves entity metadata from the target environment, utilizing 
        /// an internal cache to improve performance.
        /// </summary>
        /// <param name="logicalName">The logical name of the entity.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The entity metadata, or null if it cannot be retrieved.</returns>
        private async Task<EntityMetadata?> GetMetadataAsync(
            string logicalName,
            CancellationToken ct)
        {
            if (_metadataCache.TryGetValue(logicalName, out var meta))
            {
                return meta;
            }

            try
            {
                var newMeta = await _target
                    .GetEntityMetadataAsync(logicalName, ct);

                if (newMeta != null)
                {
                    _metadataCache[logicalName] = newMeta;
                }

                return newMeta;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    "Could not fetch metadata for {Entity}: {Msg}",
                    logicalName,
                    ex.Message
                );

                return null;
            }
        }

        /// <summary>
        /// Determines whether an attribute is forbidden from being synchronized 
        /// (e.g., system-managed fields like versionnumber).
        /// </summary>
        /// <param name="attrName">The logical name of the attribute.</param>
        /// <returns>True if the attribute is forbidden; otherwise, false.</returns>
        private bool IsForbiddenAttribute(string attrName)
        {
            var forbidden = new[]
            {
                "versionnumber",
                "createdby",
                "modifiedby",
                "createdonbehalfby",
                "modifiedonbehalfby",
                "overriddencreatedon",
                "importsequencenumber",
                "address1_addressid",
                "address2_addressid"
            };

            return forbidden.Contains(attrName.ToLower());
        }

        /// <summary>
        /// Determines whether an attribute is a user reference field (e.g., 
        /// ownerid, createdby).
        /// </summary>
        /// <param name="attrName">The logical name of the attribute.</param>
        /// <returns>True if the attribute is a user field; otherwise, false.</returns>
        private bool IsUserAttribute(string attrName)
        {
            var userFields = new[] { "ownerid", "createdby", "modifiedby" };

            return userFields.Contains(attrName.ToLower());
        }
    }
}
