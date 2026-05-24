using System.Collections.Concurrent;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Resolves user references from a source Dataverse environment to a 
   /// target environment.
   /// </summary>
   public class UserService : IUserService
   {
      private readonly IDataverseProvider? _source;
      private readonly IDataverseProvider? _target;
      private readonly ILogger _logger;

      private readonly ConcurrentDictionary<Guid, EntityReference>
         _mappingCache = new ConcurrentDictionary<Guid, EntityReference>();

      private readonly ConcurrentDictionary<Guid, UserMappingSummary>
         _summaries = new ConcurrentDictionary<Guid, UserMappingSummary>();

      /// <inheritdoc />
      public bool IsMapped { get; private set; } = false;

      /// <summary>
      /// Initializes a new instance of the <see cref="UserService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="source">Optional source Dataverse provider.</param>
      /// <param name="target">Optional target Dataverse provider.</param>
      public UserService(
         ILogger logger,
         IDataverseProvider? source = null,
         IDataverseProvider? target = null
      )
      {
         _logger = logger;
         _source = source;
         _target = target;
      }

      /// <inheritdoc />
      public void AddManualMapping(
         Guid sourceUserId,
         Guid targetUserId,
         string? sourceName = null,
         string? targetName = null,
         bool isHuman = true
      )
      {
         _mappingCache[sourceUserId] = new EntityReference(
            DataverseEntities.SystemUser.Name,
            targetUserId
         );

         _summaries[sourceUserId] = new UserMappingSummary(
            sourceName ?? sourceUserId.ToString(),
            sourceUserId,
            targetName ?? targetUserId.ToString(),
            targetUserId,
            UserMappingStatus.Manual,
            isHuman
         );
      }

      /// <inheritdoc />
      public async Task<List<Guid>> GetRealActiveUsersAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      )
      {
         try
         {
            var query = new QueryExpression(DataverseEntities.SystemUser.Name)
            {
               ColumnSet = new ColumnSet(
                  DataverseAttributes.SystemUserId,
                  DataverseAttributes.FullName
               )
            };

            query.Criteria.AddCondition(
               DataverseAttributes.IsDisabled,
               ConditionOperator.Equal,
               false
            );

            // accessmode = 0 (Read-Write/Human)
            query.Criteria.AddCondition(
               DataverseAttributes.AccessMode,
               ConditionOperator.Equal,
               0
            );

            var results = await provider.RetrieveMultipleAsync(query, ct);

            var users = results.Entities
               .Select(e => new
               {
                  Id = e.Id,
                  Name = e.GetAttributeValue<string>(
                     DataverseAttributes.FullName
                  ) ?? "Unknown"
               })
               .ToList();

            if (users.Count > 0)
            {
               var names = string.Join(", ", users.Select(u => u.Name));

               _logger.Information(
                  $"Found {users.Count} users for randomization: {names}"
               );
            }
            else
            {
               _logger.Warning(
                  "No active Read-Write users found for randomization. " +
                  "Creations will use the default caller."
               );
            }

            return users.Select(u => u.Id).ToList();
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to retrieve available users for randomization. " +
               "Defaulting to execution user."
            );

            return new List<Guid>();
         }
      }

      /// <inheritdoc />
      public void ClearCache()
      {
         _mappingCache.Clear();
         _summaries.Clear();
      }

      /// <inheritdoc />
      public async Task MapAllSourceUsersAsync(CancellationToken ct = default)
      {
         if (IsMapped)
            return;

         if (_source == null)
         {
            throw new InvalidOperationException(
               "Source provider must be set to map all source users."
            );
         }

         _logger.Information("Mapping users...".t());

         var query = new QueryExpression(DataverseEntities.SystemUser.Name)
         {
            ColumnSet = new ColumnSet(
               DataverseAttributes.InternalEmailAddress,
               DataverseAttributes.DomainName,
               DataverseAttributes.FullName,
               DataverseAttributes.SystemUserId,
               DataverseAttributes.AccessMode,
               DataverseAttributes.FirstName
            )
         };

         query.Criteria.AddCondition(
            DataverseAttributes.IsDisabled,
            ConditionOperator.Equal,
            false
         );

         var results = await _source.RetrieveMultipleAsync(query, ct);

         foreach (var user in results.Entities)
            await MapUserInternalAsync(user, ct);

         IsMapped = true;
      }

      /// <inheritdoc />
      public Task<List<UserMappingSummary>> GetMappingSummaryAsync(
         CancellationToken ct = default
      )
      {
         return Task.FromResult(
            _summaries.Values.OrderBy(s => s.SourceName).ToList()
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

         if (_source == null)
         {
            throw new InvalidOperationException(
               "Source provider must be set to map a user."
            );
         }

         _logger.Debug("Attempting to map source user {Id}", sourceUser.Id);

         var sourceUserData = await _source.RetrieveAsync(
            DataverseEntities.SystemUser.Name,
            sourceUser.Id,
            new[]
            {
               DataverseAttributes.InternalEmailAddress,
               DataverseAttributes.DomainName,
               DataverseAttributes.FullName,
               DataverseAttributes.AccessMode,
               DataverseAttributes.FirstName
            },
            ct
         );

         if (sourceUserData == null)
         {
            _logger.Warning("Source user {Id} not found.", sourceUser.Id);

            return null;
         }

         return await MapUserInternalAsync(sourceUserData, ct);
      }

      private async Task<EntityReference?> MapUserInternalAsync(
         Entity sourceUserData,
         CancellationToken ct
      )
      {
         var sourceUserId = sourceUserData.Id;

         if (_mappingCache.TryGetValue(sourceUserId, out var cached))
            return cached;

         var sourceFullName = sourceUserData.GetAttributeValue<string>(
            DataverseAttributes.FullName
         ) ?? "Unknown Source User";

         var accessMode = sourceUserData.GetAttributeValue<OptionSetValue>(
            DataverseAttributes.AccessMode
         );

         bool isHuman = accessMode?.Value == 0 &&
            !sourceFullName.StartsWith("#");

         var email = sourceUserData.GetAttributeValue<string>(
            DataverseAttributes.InternalEmailAddress
         );

         if (!string.IsNullOrEmpty(email))
         {
            _logger.Debug(
               "Searching for target user by email: {Email}",
               email
            );

            var mapped = await FindTargetUserAsync(
               DataverseAttributes.InternalEmailAddress,
               email,
               ct
            );

            if (mapped.HasValue)
            {
               _logger.Debug(
                  "Found match by email for user {Id}",
                  sourceUserId
               );

               _mappingCache[sourceUserId] = mapped.Value.UserRef;

               _summaries[sourceUserId] = new UserMappingSummary(
                  sourceFullName,
                  sourceUserId,
                  mapped.Value.FullName,
                  mapped.Value.UserRef.Id,
                  UserMappingStatus.Mapped,
                  isHuman
               );

               return mapped.Value.UserRef;
            }
         }

         var domainName = sourceUserData.GetAttributeValue<string>(
            DataverseAttributes.DomainName
         );

         if (!string.IsNullOrEmpty(domainName))
         {
            _logger.Debug(
               "Searching for target user by domain name: {Domain}",
               domainName
            );

            var mapped = await FindTargetUserAsync(
               DataverseAttributes.DomainName,
               domainName,
               ct
            );

            if (mapped.HasValue)
            {
               _logger.Debug(
                  "Found match by domain name for user {Id}",
                  sourceUserId
               );

               _mappingCache[sourceUserId] = mapped.Value.UserRef;

               _summaries[sourceUserId] = new UserMappingSummary(
                  sourceFullName,
                  sourceUserId,
                  mapped.Value.FullName,
                  mapped.Value.UserRef.Id,
                  UserMappingStatus.Mapped,
                  isHuman
               );

               return mapped.Value.UserRef;
            }
         }

         if (isHuman)
         {
            _logger.Warning(
               "Could not map source user {FullName} ({Id})",
               sourceFullName,
               sourceUserId
            );
         }

         _summaries[sourceUserId] = new UserMappingSummary(
            sourceFullName,
            sourceUserId,
            "Caller (Default)",
            Guid.Empty,
            UserMappingStatus.Unmapped,
            isHuman
         );

         return null;
      }

      private async Task<(EntityReference UserRef, string FullName)?>
         FindTargetUserAsync(
            string attribute,
            string value,
            CancellationToken ct
         )
      {
         if (_target == null)
         {
            throw new InvalidOperationException(
               "Target provider must be set to find target users."
            );
         }

         var query = new QueryByAttribute(DataverseEntities.SystemUser.Name)
         {
            ColumnSet = new ColumnSet(
               DataverseAttributes.SystemUserId,
               DataverseAttributes.FullName
            )
         };

         query.AddAttributeValue(attribute, value);

         var results = await _target.RetrieveMultipleAsync(query, ct);
         var user = results.Entities.FirstOrDefault();

         if (user != null)
         {
            var fullName = user.GetAttributeValue<string>(
               DataverseAttributes.FullName
            ) ?? "Unknown Target User";

            return (user.ToEntityReference(), fullName);
         }

         return null;
      }
   }
}
