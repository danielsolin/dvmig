using System.Collections.Concurrent;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Maps user references from a source Dataverse environment to a target 
    /// environment. This resolves differences in systemuser GUIDs between 
    /// environments by matching users based on their internal email address 
    /// or domain name.
    /// </summary>
    public class UserMapper : IUserMapper
    {
        private readonly IDataverseProvider _source;
        private readonly IDataverseProvider _target;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<Guid, EntityReference>
            _mappingCache = new ConcurrentDictionary<Guid, EntityReference>();

        public UserMapper(
            IDataverseProvider source,
            IDataverseProvider target,
            ILogger logger
        )
        {
            _source = source;
            _target = target;
            _logger = logger;
        }

        /// <summary>
        /// Manually adds a user mapping to the cache, bypassing the automatic 
        /// lookup. Useful for handling known edge cases or system accounts.
        /// </summary>
        /// <param name="sourceUserId">
        /// The GUID of the user in the source environment.
        /// </param>
        /// <param name="targetUserId">
        /// The GUID of the corresponding user in the target environment.
        /// </param>
        public void AddManualMapping(
            Guid sourceUserId,
            Guid targetUserId
        )
        {
            _mappingCache[sourceUserId] = new EntityReference(
                "systemuser",
                targetUserId
            );
        }

        /// <summary>
        /// Asynchronously maps a source user reference to the corresponding 
        /// target user reference. Queries the source environment for the 
        /// user's email or domain name, and then searches the target 
        /// environment for a matching user. Results are cached to improve 
        /// performance.
        /// </summary>
        /// <param name="sourceUser">
        /// The entity reference of the user from the source environment.
        /// </param>
        /// <param name="ct">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>
        /// An entity reference for the mapped user in the target environment, 
        /// or null if mapping fails.
        /// </returns>
        public async Task<EntityReference?> MapUserAsync(
            EntityReference? sourceUser,
            CancellationToken ct = default
        )
        {
            if (sourceUser == null)
            {
                return null;
            }

            if (_mappingCache.TryGetValue(sourceUser.Id, out var targetRef))
            {
                return targetRef;
            }

            _logger.Debug("Attempting to map source user {Id}", sourceUser.Id);

            var sourceUserData = await _source.RetrieveAsync(
                "systemuser",
                sourceUser.Id,
                new[] { "internalemailaddress", "domainname", "fullname" },
                ct
            );

            if (sourceUserData == null)
            {
                _logger.Warning("Source user {Id} not found.", sourceUser.Id);

                return null;
            }

            var email = sourceUserData
                .GetAttributeValue<string>("internalemailaddress");
            if (!string.IsNullOrEmpty(email))
            {
                var mapped = await FindTargetUserAsync(
                    "internalemailaddress",
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
                .GetAttributeValue<string>("domainname");
            if (!string.IsNullOrEmpty(domainName))
            {
                var mapped = await FindTargetUserAsync(
                    "domainname",
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
                sourceUserData.GetAttributeValue<string>("fullname"),
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
            var query = new QueryByAttribute("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid")
            };
            query.AddAttributeValue(attribute, value);

            var results = await _target.RetrieveMultipleAsync(query, ct);
            var user = results.Entities.FirstOrDefault();

            if (user != null)
            {
                return user.ToEntityReference();
            }

            return null;
        }
    }
}
