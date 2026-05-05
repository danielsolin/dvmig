using System.Collections.Concurrent;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IEntityService"/> that handles 
   /// entity preparation, metadata management, and record associations.
   /// </summary>
   public class EntityService : IEntityService
   {
      private readonly ILogger _logger;
      private readonly IDataverseProvider? _target;

      private readonly ConcurrentDictionary<string, EntityMetadata> _metaCache =
         new ConcurrentDictionary<string, EntityMetadata>();

      /// <summary>
      /// Initializes a new instance of the <see cref="EntityService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="target">
      /// Optional target provider for cached metadata operations.
      /// </param>
      public EntityService(ILogger logger, IDataverseProvider? target = null)
      {
         _logger = logger;
         _target = target;
      }

      #region IEntityService Implementation (Preparation)

      /// <inheritdoc />
      public async Task<Entity> PrepareEntityForTargetAsync(
         Entity sourceEntity,
         EntityMetadata metadata,
         SyncOptions options,
         IUserResolver userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct = default
      )
      {
         var targetEntity = new Entity(
            sourceEntity.LogicalName,
            sourceEntity.Id
         );

         foreach (var attribute in sourceEntity.Attributes)
         {
            if (IsForbiddenAttribute(attribute.Key))
               continue;

            var attrMetadata = metadata.Attributes?
               .FirstOrDefault(a => a.LogicalName == attribute.Key);

            if (attrMetadata != null &&
                attrMetadata.IsValidForCreate == false &&
                attrMetadata.IsValidForUpdate == false)
               continue;

            var value = attribute.Value;

            if (value is EntityReference er)
            {
               if (IsUserAttribute(attribute.Key))
               {
                  value = await userResolver.MapUserAsync(er, ct);

                  if (value == null)
                  {
                     _logger.Warning(
                        "Skipping unmapped user field {Attr} for " +
                        "{Entity}:{Id}; source user {UserId} was " +
                        "not found or could not be resolved.",
                        attribute.Key,
                        sourceEntity.LogicalName,
                        sourceEntity.Id,
                        er.Id
                     );

                     continue;
                  }
               }
               else if (idMappingCache.TryGetValue(
                  EntityHelper.GetRecordKey(er),
                  out var mappedId))
                  value = new EntityReference(er.LogicalName, mappedId);
            }
            else if (value is EntityCollection collection &&
                     collection.Entities.Count > 0 &&
                     collection.Entities[0].LogicalName ==
                        SystemConstants.DataverseEntities.ActivityParty)
            {
               value = await PrepareActivityPartyCollectionAsync(
                  collection,
                  sourceEntity,
                  userResolver,
                  idMappingCache,
                  ct
               );
            }

            targetEntity[attribute.Key] = value;
         }

         targetEntity[SystemConstants.DataverseAttributes
            .ImportSequenceNumber] = 1;

         return targetEntity;
      }

      private async Task<EntityCollection> PrepareActivityPartyCollectionAsync(
         EntityCollection collection,
         Entity sourceEntity,
         IUserResolver userResolver,
         ConcurrentDictionary<string, Guid> idMappingCache,
         CancellationToken ct
      )
      {
         var newCollection = new EntityCollection
         {
            EntityName = collection.EntityName
         };

         foreach (var party in collection.Entities)
         {
            var targetParty = new Entity(
               SystemConstants.DataverseEntities.ActivityParty
            );
            bool skipParty = false;

            foreach (var partyAttr in party.Attributes)
            {
               bool isPartyId = partyAttr.Key ==
                  SystemConstants.DataverseAttributes.PartyId;
               bool isTypeMask = partyAttr.Key ==
                  SystemConstants.DataverseAttributes
                     .ParticipationTypeMask;
               bool isAddress = partyAttr.Key ==
                  SystemConstants.DataverseAttributes.AddressUsed;

               if (!isPartyId && !isTypeMask && !isAddress)
                  continue;

               var partyValue = partyAttr.Value;

               if (partyAttr.Key ==
                      SystemConstants.DataverseAttributes.PartyId &&
                   partyValue is EntityReference pr)
               {
                  if (pr.LogicalName ==
                         SystemConstants.DataverseEntities.SystemUser)
                  {
                     partyValue = await userResolver.MapUserAsync(pr, ct);

                     if (partyValue == null)
                     {
                        _logger.Warning(
                           "Skipping unmapped user partyid for " +
                           "{Entity}:{Id}; source user {UserId} was " +
                           "not found or could not be resolved.",
                           sourceEntity.LogicalName,
                           sourceEntity.Id,
                           pr.Id
                        );

                        skipParty = true;
                        break;
                     }
                  }
                  else if (idMappingCache.TryGetValue(
                     EntityHelper.GetRecordKey(pr),
                     out var mappedId))
                  {
                     partyValue = new EntityReference(
                        pr.LogicalName,
                        mappedId
                     );
                  }
               }

               targetParty[partyAttr.Key] = partyValue;
            }

            if (!skipParty)
               newCollection.Entities.Add(targetParty);
         }

         return newCollection;
      }

      /// <inheritdoc />
      public async Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         var metadata = await GetMetadataAsync(entity.LogicalName, ct);

         if (metadata == null)
            return null;

         var primaryNameAttr = metadata.PrimaryNameAttribute;

         if (string.IsNullOrEmpty(primaryNameAttr) ||
             !entity.Contains(primaryNameAttr))
            return null;

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
         return SystemConstants.ForbiddenAttributes.Contains(attributeName);
      }

      /// <inheritdoc />
      public bool IsUserAttribute(string attributeName)
      {
         return SystemConstants.UserAttributes.Contains(attributeName);
      }

      /// <inheritdoc />
      public async Task<HashSet<Guid>> GetAllIdsAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      )
      {
         var query = new QueryExpression(logicalName)
         {
            ColumnSet = new ColumnSet(false)
         };

         var results = await provider.RetrieveMultipleAsync(query, ct);

         return new HashSet<Guid>(results.Entities.Select(e => e.Id));
      }

      #endregion

      #region IEntityService Implementation (Metadata)

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         if (_metaCache.TryGetValue(entityLogicalName, out var meta))
            return meta;

         if (_target == null)
            throw new InvalidOperationException(
               "Target provider is required for cached metadata retrieval."
            );

         try
         {
            var newMeta = await _target
               .GetEntityMetadataAsync(entityLogicalName, ct);

            if (newMeta != null)
               _metaCache[entityLogicalName] = newMeta;

            return newMeta;
         }
         catch (Exception ex)
         {
            _logger.Warning(
               "Could not fetch metadata for {Entity}: {Msg}",
               entityLogicalName,
               ex.Message
            );

            return null;
         }
      }

      /// <inheritdoc />
      public async Task<ColumnSet> GetValidColumnsAsync(
         string logicalName,
         CancellationToken ct = default
      )
      {
         var meta = await GetMetadataAsync(logicalName, ct);

         if (meta == null || meta.Attributes == null)
            return new ColumnSet(true);

         var whitelist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
         )
         {
            meta.PrimaryIdAttribute,
            meta.PrimaryNameAttribute ?? string.Empty,
            DataverseAttributes.OwnerId,
            DataverseAttributes.StateCode,
            DataverseAttributes.StatusCode,
            DataverseAttributes.CreatedOn,
            DataverseAttributes.ModifiedOn,
            DataverseAttributes.TransactionCurrencyId,
            DataverseAttributes.ExchangeRate,
            DataverseAttributes.CreatedBy,
            DataverseAttributes.ModifiedBy
         };

         var attributes = meta.Attributes
            .Where(a =>
               whitelist.Contains(a.LogicalName) ||
               a.AttributeType == AttributeTypeCode.PartyList ||
               (a.IsLogical == false &&
                a.IsValidForRead == true &&
                (a.IsValidForCreate == true ||
                 a.IsValidForUpdate == true)))
            .Select(a => a.LogicalName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToArray();

         if (attributes.Length == 0)
            return new ColumnSet(true);

         _logger.Debug(
            "Configured ColumnSet for {Entity} with {Count} attributes.",
            logicalName,
            attributes.Length
         );

         return new ColumnSet(attributes);
      }

      /// <inheritdoc />
      public async Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      )
      {
         var request = new RetrieveAllEntitiesRequest
         {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
         };

         var response = (RetrieveAllEntitiesResponse)await
            provider.ExecuteAsync(request, ct);

         var entities = response.EntityMetadata
            .Where(e =>
               e.IsIntersect == false &&
               e.IsValidForAdvancedFind == true &&
               e.IsImportable == true &&
               e.IsLogicalEntity == false)
            .OrderBy(e =>
               e.DisplayName?.UserLocalizedLabel?.Label ??
               e.LogicalName)
            .ToList();

         return entities;
      }

      /// <inheritdoc />
      public void ClearMetadataCache()
      {
         _metaCache.Clear();
      }

      #endregion

      #region IEntityService Implementation (Relationships)

      /// <inheritdoc />
      public async Task AssociateAsync(
         IDataverseProvider target,
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         var references = entity.Attributes
            .Values.OfType<EntityReference>().ToList();

         if (references.Count < 2)
         {
            _logger.Warning(
               "Intersect entity {Key} does not have " +
               "two EntityReferences.",
               entity.LogicalName
            );

            throw new System.InvalidOperationException(
               "Invalid N:N relationship record."
            );
         }

         var request = new AssociateRequest
         {
            Target = references[0],
            Relationship = new Relationship(entity.LogicalName),
            RelatedEntities = new EntityReferenceCollection
            {
               references[1]
            }
         };

         await target.ExecuteAsync(request, ct, callerId);
      }

      #endregion
   }
}
