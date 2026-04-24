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
            EntityMetadata? metadata,
            SyncOptions options,
            CancellationToken ct)
        {
            var target = new Entity(entity.LogicalName, entity.Id);

            foreach (var attr in entity.Attributes)
            {
                if (IsForbiddenAttribute(attr.Key))
                {
                    continue;
                }

                if (IsUserAttribute(attr.Key) &&
                    attr.Value is EntityReference userRef)
                {
                    var mapped = await _userMapper.MapUserAsync(userRef, ct);
                    if (mapped != null)
                    {
                        target[attr.Key] = mapped;

                        continue;
                    }

                    // Mapping failed. Do NOT send original user ID to target
                    _logger.Warning(
                        "Skipping user attribute '{Attr}' for {Key}:{Id} " +
                        "because source user could not be mapped.",
                        attr.Key,
                        entity.LogicalName,
                        entity.Id
                    );

                    continue;
                }

                if (metadata?.Attributes != null)
                {
                    var attrMeta = metadata.Attributes
                        .FirstOrDefault(a => a.LogicalName == attr.Key);

                    if (attrMeta != null &&
                        attrMeta.IsValidForCreate == false &&
                        attr.Key != "statecode" &&
                        attr.Key != "statuscode")
                    {
                        _logger.Debug(
                            "Proactively stripping {Attr} for {Entity}",
                            attr.Key,
                            entity.LogicalName
                        );

                        continue;
                    }
                }

                if (attr.Value is EntityReference er)
                {
                    var cacheKey = $"{er.LogicalName}:{er.Id}";
                    if (_idMappingCache.TryGetValue(cacheKey, out var targetId))
                    {
                        target[attr.Key] = new EntityReference(
                            er.LogicalName,
                            targetId
                        );

                        continue;
                    }
                }

                target[attr.Key] = attr.Value;
            }

            return target;
        }

        /// <summary>
        /// Attempts to find an existing record in the target environment that 
        /// matches the source entity using alternate keys, primary name 
        /// attribute, or specific entity-based fallback logic.
        /// </summary>
        /// <param name="sourceEntity">The source entity record.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// The GUID of the matching target record if found; otherwise, null.
        /// </returns>
        private async Task<Guid?> FindExistingOnTargetAsync(
            Entity sourceEntity,
            CancellationToken ct)
        {
            var metadata = await GetMetadataAsync(
                sourceEntity.LogicalName,
                ct
            );

            if (metadata == null)
            {
                return null;
            }

            // 1. Try Alternate Keys
            if (metadata.Keys != null && metadata.Keys.Any())
            {
                foreach (var key in metadata.Keys)
                {
                    var query = new QueryExpression(sourceEntity.LogicalName)
                    {
                        ColumnSet = new ColumnSet(false)
                    };
                    var hasAllParts = true;

                    foreach (var attrName in key.KeyAttributes)
                    {
                        if (!sourceEntity.Contains(attrName))
                        {
                            hasAllParts = false;

                            break;
                        }

                        query.Criteria.AddCondition(
                            attrName,
                            ConditionOperator.Equal,
                            sourceEntity[attrName]
                        );
                    }

                    if (hasAllParts)
                    {
                        var results = await _target
                            .RetrieveMultipleAsync(query, ct);

                        if (results.Entities.Count == 1)
                        {
                            return results.Entities.First().Id;
                        }

                        if (results.Entities.Count > 1)
                        {
                            _logger.Warning(
                                "Ambiguous alternate key match for {Entity}. " +
                                "Found {Count} records. Skipping mapping.",
                                sourceEntity.LogicalName,
                                results.Entities.Count
                            );
                        }
                    }
                }
            }

            // 2. Fallback to Primary Name Attribute
            var primaryAttr = metadata.PrimaryNameAttribute;
            if (!string.IsNullOrEmpty(primaryAttr) &&
                sourceEntity.Contains(primaryAttr))
            {
                var query = new QueryExpression(sourceEntity.LogicalName)
                {
                    ColumnSet = new ColumnSet(false)
                };
                query.Criteria.AddCondition(
                    primaryAttr,
                    ConditionOperator.Equal,
                    sourceEntity[primaryAttr]
                );

                var results = await _target.RetrieveMultipleAsync(query, ct);
                if (results.Entities.Count == 1)
                {
                    return results.Entities.First().Id;
                }

                if (results.Entities.Count > 1)
                {
                    _logger.Warning(
                        "Ambiguous primary name match for {Entity} '{Name}'. " +
                        "Found {Count} records. Skipping mapping.",
                        sourceEntity.LogicalName,
                        sourceEntity[primaryAttr],
                        results.Entities.Count
                    );
                }
            }

            // 3. Fallback for specific entities with unique constraints 
            // but no alternate keys
            if (sourceEntity.LogicalName == "transactioncurrency" &&
                sourceEntity.Contains("isocurrencycode"))
            {
                var query = new QueryExpression(sourceEntity.LogicalName)
                {
                    ColumnSet = new ColumnSet(false)
                };
                query.Criteria.AddCondition(
                    "isocurrencycode",
                    ConditionOperator.Equal,
                    sourceEntity["isocurrencycode"]
                );

                var results = await _target.RetrieveMultipleAsync(query, ct);
                if (results.Entities.Count == 1)
                {
                    return results.Entities.First().Id;
                }
            }

            return null;
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
