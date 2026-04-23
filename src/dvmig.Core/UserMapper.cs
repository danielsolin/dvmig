using Microsoft.Xrm.Sdk;
using System.Collections.Concurrent;
using dvmig.Providers;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core
{
    public interface IUserMapper
    {
        Task<EntityReference?> MapUserAsync(
            EntityReference? sourceUser,
            CancellationToken ct = default);

        void AddManualMapping(Guid sourceUserId, Guid targetUserId);
    }

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
            ILogger logger)
        {
            _source = source;
            _target = target;
            _logger = logger;
        }

        public void AddManualMapping(Guid sourceUserId, Guid targetUserId)
        {
            _mappingCache[sourceUserId] = new EntityReference(
                "systemuser",
                targetUserId
            );
        }

        public async Task<EntityReference?> MapUserAsync(
            EntityReference? sourceUser,
            CancellationToken ct = default)
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

            _logger.Warning("" +
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
