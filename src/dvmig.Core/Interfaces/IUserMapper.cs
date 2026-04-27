using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for a service that maps user references from a 
    /// source environment to the corresponding users in a target environment.
    /// This resolves differences in systemuser GUIDs between environments 
    /// by matching users based on their internal email address or domain name.
    /// </summary>
    public interface IUserMapper
    {
        /// <summary>
        /// Asynchronously maps a source user reference to its equivalent 
        /// in the target environment.
        /// </summary>
        /// <param name="sourceUser">
        /// The user reference from the source environment.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// An entity reference to the mapped user in the target environment, 
        /// or null if mapping fails.
        /// </returns>
        Task<EntityReference?> MapUserAsync(
            EntityReference? sourceUser,
            CancellationToken ct = default
        );

        /// <summary>
        /// Manually adds a user mapping, bypassing automatic lookup.
        /// </summary>
        /// <param name="sourceUserId">The ID of the source user.</param>
        /// <param name="targetUserId">The ID of the target user.</param>
        void AddManualMapping(Guid sourceUserId, Guid targetUserId);
    }
}
