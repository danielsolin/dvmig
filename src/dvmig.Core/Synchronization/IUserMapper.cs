using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
    public interface IUserMapper
    {
        Task<EntityReference?> MapUserAsync(
            EntityReference? sourceUser,
            CancellationToken ct = default
        );

        void AddManualMapping(Guid sourceUserId, Guid targetUserId);
    }
}
