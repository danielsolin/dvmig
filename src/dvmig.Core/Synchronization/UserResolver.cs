using dvmig.Core.Interfaces;
using System.Collections.Concurrent;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Resolves user references from a source Dataverse environment to a target 
   /// environment.
   /// </summary>
   public class UserResolver : IUserResolver
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly ILogger _logger;

      private readonly ConcurrentDictionary<Guid, EntityReference>
         _mappingCache = new ConcurrentDictionary<Guid, EntityReference>();

      /// <summary>
      /// Initializes a new instance of the <see cref="UserResolver"/> class.
      /// </summary>
      /// <param name="source">The source Dataverse provider.</param>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="logger">The logger instance.</param>
      public UserResolver(
         IDataverseProvider source,
         IDataverseProvider target,
         ILogger logger
      )
      {
         _source = source;
         _target = target;
         _logger = logger;
      }

      /// <inheritdoc />
      public void AddManualMapping(Guid sourceUserId, Guid targetUserId)
      {
         _mappingCache[sourceUserId] = new EntityReference(
            SystemConstants.DataverseEntities.SystemUser,
            targetUserId
         );
      }

      /// <inheritdoc />
      public async Task<EntityReference?> MapUserAsync(
         EntityReference? sourceUser,
         CancellationToken ct = default
      )
      {
         if (sourceUser == null)
            return null;

         if (_mappingCache.TryGetValue(sourceUser.Id, out var targetRef))
            return targetRef;

         _logger.Debug("Attempting to map source user {Id}", sourceUser.Id);

         var sourceUserData = await _source.RetrieveAsync(
            SystemConstants.DataverseEntities.SystemUser,
            sourceUser.Id,
            new[]
            {
               SystemConstants.DataverseAttributes.InternalEmailAddress,
               SystemConstants.DataverseAttributes.DomainName,
               SystemConstants.DataverseAttributes.FullName
            },
            ct
         );

         if (sourceUserData == null)
         {
            _logger.Warning("Source user {Id} not found.", sourceUser.Id);

            return null;
         }

         var email = sourceUserData
            .GetAttributeValue<string>(
               SystemConstants.DataverseAttributes.InternalEmailAddress
            );

         if (!string.IsNullOrEmpty(email))
         {
            var mapped = await FindTargetUserAsync(
               SystemConstants.DataverseAttributes.InternalEmailAddress,
               email,
               ct
            );

            if (mapped != null)
            {
               _mappingCache[sourceUser.Id] = mapped;

               return mapped;
            }
         }

         var domainName = sourceUserData
            .GetAttributeValue<string>(
               SystemConstants.DataverseAttributes.DomainName
            );

         if (!string.IsNullOrEmpty(domainName))
         {
            var mapped = await FindTargetUserAsync(
               SystemConstants.DataverseAttributes.DomainName,
               domainName,
               ct
            );

            if (mapped != null)
            {
               _mappingCache[sourceUser.Id] = mapped;

               return mapped;
            }
         }

         _logger.Warning(
            "Could not map source user {FullName} ({Id})",
            sourceUserData.GetAttributeValue<string>(
               SystemConstants.DataverseAttributes.FullName
            ),
            sourceUser.Id
         );

         return null;
      }

      private async Task<EntityReference?> FindTargetUserAsync(
         string attribute,
         string value,
         CancellationToken ct
      )
      {
         var query = new QueryByAttribute(
            SystemConstants.DataverseEntities.SystemUser
         )
         {
            ColumnSet = new ColumnSet(
               SystemConstants.DataverseAttributes.SystemUserId
            )
         };

         query.AddAttributeValue(attribute, value);

         var results = await _target.RetrieveMultipleAsync(query, ct);
         var user = results.Entities.FirstOrDefault();

         if (user != null)
            return user.ToEntityReference();

         return null;
      }
   }
}
