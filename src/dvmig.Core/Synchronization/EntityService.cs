using dvmig.Core.Interfaces;
using System.Collections.Concurrent;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IEntityService"/> that prepares entities
   /// for synchronization to the target environment.
   /// </summary>
   public class EntityService : IEntityService
   {
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="EntityService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public EntityService(ILogger logger)
      {
         _logger = logger;
      }

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
                  $"{er.LogicalName}:{er.Id}",
                  out var mappedId))
                  value = new EntityReference(er.LogicalName, mappedId);
            }
            else if (value is EntityCollection collection &&
                     collection.Entities.Count > 0 &&
                     collection.Entities[0].LogicalName ==
                        SystemConstants.DataverseEntities.ActivityParty)
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
                           $"{pr.LogicalName}:{pr.Id}",
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

               value = newCollection;
            }

            targetEntity[attribute.Key] = value;
         }

         return targetEntity;
      }

      /// <inheritdoc />
      public async Task<Guid?> FindExistingOnTargetAsync(
         Entity entity,
         IDataverseProvider target,
         Func<string, CancellationToken, Task<EntityMetadata?>>
            getMetadataFunc,
         CancellationToken ct = default
      )
      {
         var metadata = await getMetadataFunc(entity.LogicalName, ct);

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
         var forbidden = new[]
         {
            SystemConstants.DataverseAttributes.VersionNumber,
            SystemConstants.DataverseAttributes.CreatedBy,
            SystemConstants.DataverseAttributes.ModifiedBy,
            SystemConstants.DataverseAttributes.CreatedOnBehalfBy,
            SystemConstants.DataverseAttributes.ModifiedOnBehalfBy,
            SystemConstants.DataverseAttributes.OverriddenCreatedOn,
            SystemConstants.DataverseAttributes.ImportSequenceNumber,
            SystemConstants.DataverseAttributes.Address1Id,
            SystemConstants.DataverseAttributes.Address2Id
         };

         return forbidden.Contains(attributeName.ToLower());
      }

      /// <inheritdoc />
      public bool IsUserAttribute(string attributeName)
      {
         var userFields = new[]
         {
            SystemConstants.DataverseAttributes.OwnerId,
            SystemConstants.DataverseAttributes.CreatedBy,
            SystemConstants.DataverseAttributes.ModifiedBy
         };

         return userFields.Contains(attributeName.ToLower());
      }

      /// <inheritdoc />
      public async Task<HashSet<Guid>> GetAllIdsAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      )
      {
         var ids = new HashSet<Guid>();
         var query = new QueryExpression(logicalName)
         {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo
            {
               Count = 5000,
               PageNumber = 1
            }
         };

         while (true)
         {
            var results = await provider.RetrieveMultipleAsync(query, ct);
            foreach (var entity in results.Entities)
               ids.Add(entity.Id);

            if (!results.MoreRecords)
               break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = results.PagingCookie;
         }

         return ids;
      }
   }
}
