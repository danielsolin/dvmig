using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Represents a summary of a user mapping from source to target.
   /// </summary>
   public record UserMappingSummary(
      string SourceName,
      Guid SourceId,
      string TargetName,
      Guid TargetId,
      string Status,
      bool IsHuman = false
   );

   /// <summary>
   /// Defines the contract for a service that resolves user references from 
   /// a source environment to the corresponding users in a target environment.
   /// </summary>
   public interface IUserResolver
   {
      /// <summary>
      /// Asynchronously resolves a source user reference to its equivalent 
      /// in the target environment.
      /// </summary>
      /// <param name="sourceUser">
      /// The user reference from the source environment.
      /// </param>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>
      /// An entity reference to the resolved user in the target environment, 
      /// or null if resolution fails.
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

      /// <summary>
      /// Retrieves a summary of all user mappings resolved so far.
      /// </summary>
      /// <param name="ct">A cancellation token.</param>
      /// <returns>A list of user mapping summaries.</returns>
      Task<List<UserMappingSummary>> GetMappingSummaryAsync(
         CancellationToken ct = default
      );

      /// <summary>
      /// Proactively maps all active users from the source environment.
      /// </summary>
      /// <param name="ct">A cancellation token.</param>
      Task MapAllSourceUsersAsync(CancellationToken ct = default);

      /// <summary>
      /// Clears the user mapping cache.
      /// </summary>
      void ClearCache();
   }
}
