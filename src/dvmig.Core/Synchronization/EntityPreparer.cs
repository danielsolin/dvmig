using System.Collections.Concurrent;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Implementation of <see cref="IEntityPreparer"/> that prepares entities
    /// for synchronization to the target environment.
    /// </summary>
    public class EntityPreparer : IEntityPreparer
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="EntityPreparer"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public EntityPreparer(ILogger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<Entity> PrepareEntityForTargetAsync(
            Entity sourceEntity,
            EntityMetadata metadata,
            SyncOptions options,
            IUserMapper userMapper,
            ConcurrentDictionary<string, Guid> idMappingCache,
            CancellationToken ct = default)
        {
            var targetEntity = new Entity(sourceEntity.LogicalName, sourceEntity.Id);

            foreach (var attribute in sourceEntity.Attributes)
            {
                if (IsForbiddenAttribute(attribute.Key))
                {
                    continue;
                }

                var attrMetadata = metadata.Attributes?
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
                        value = await userMapper.MapUserAsync(er, ct);
                        if (value == null)
                        {
                            _logger.Warning(
                                "Skipping unmapped user field {Attr} for {Entity}:{Id}; " +
                                "source user {UserId} was not found or could not be resolved.",
                                attribute.Key,
                                sourceEntity.LogicalName,
                                sourceEntity.Id,
                                er.Id
                            );

                            continue;
                        }
                    }
                    else if (idMappingCache.TryGetValue(
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

        /// <inheritdoc />
        public async Task<Guid?> FindExistingOnTargetAsync(
            Entity entity,
            IDataverseProvider target,
            Func<string, CancellationToken, Task<EntityMetadata?>> getMetadataFunc,
            CancellationToken ct = default)
        {
            var metadata = await getMetadataFunc(entity.LogicalName, ct);
            if (metadata == null)
            {
                return null;
            }

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

            var results = await target.RetrieveMultipleAsync(query, ct);

            return results.Entities.FirstOrDefault()?.Id;
        }

        /// <inheritdoc />
        public bool IsForbiddenAttribute(string attributeName)
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

            return forbidden.Contains(attributeName.ToLower());
        }

        /// <inheritdoc />
        public bool IsUserAttribute(string attributeName)
        {
            var userFields = new[] { "ownerid", "createdby", "modifiedby" };

            return userFields.Contains(attributeName.ToLower());
        }
    }
}