using System.Collections.Concurrent;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for resolving missing dependencies during
   /// entity synchronization.
   /// </summary>
   public interface IDependencyResolver
   {
      /// <summary>
      /// Attempts to resolve missing Dataverse dependencies (e.g., missing
      /// lookup records) by identifying the missing record, synchronizing
      /// it to the target, and retrying the parent record.
      /// </summary>
      /// <param name="ex">
      /// The exception indicating the missing record.
      /// </param>
      /// <param name="entity">The parent entity record.</param>
      /// <param name="options">The synchronization configuration.</param>
      /// <param name="progress">The progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="syncRecordFunc">
      /// A function that synchronizes a missing record to the target.
      /// </param>
      /// <param name="retryEntityFunc">
      /// A function that retries the parent entity synchronization.
      /// </param>
      /// <param name="findExistingFunc">
      /// A function that finds an existing record on the target.
      /// </param>
      /// <param name="idMappingCache">
      /// A cache for storing ID mappings between source and target.
      /// </param>
      /// <param name="triedDependencies">
      /// A set tracking which dependencies have been attempted for a record.
      /// </param>
      /// <returns>
      /// True if the dependency was resolved and the parent record was
      /// successfully retried; otherwise, false.
      /// </returns>
      Task<bool> ResolveMissingDependencyAsync(
         Exception ex,
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default,
         Func<Entity, SyncOptions, CancellationToken,
            Task<(bool Success, string? FailureMessage)>>?
            syncRecordFunc = null,
         Func<Entity, SyncOptions, CancellationToken,
            Task<bool>>?
            retryEntityFunc = null,
         Func<Entity, CancellationToken, Task<Guid?>>?
            findExistingFunc = null,
         ConcurrentDictionary<string, Guid>? idMappingCache = null,
         ConcurrentDictionary<string, HashSet<string>>?
            triedDependencies = null
      );

      /// <summary>
      /// Attempts to resolve missing SQL-level dependencies (foreign key
      /// constraints) by identifying the missing record, synchronizing
      /// it to the target, and retrying the parent record.
      /// </summary>
      /// <param name="message">The SQL error message.</param>
      /// <param name="entity">The parent entity record.</param>
      /// <param name="options">The synchronization configuration.</param>
      /// <param name="progress">The progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      /// <param name="syncRecordFunc">
      /// A function that synchronizes a missing record to the target.
      /// </param>
      /// <param name="retryEntityFunc">
      /// A function that retries the parent entity synchronization.
      /// </param>
      /// <param name="findExistingFunc">
      /// A function that finds an existing record on the target.
      /// </param>
      /// <param name="idMappingCache">
      /// A cache for storing ID mappings between source and target.
      /// </param>
      /// <returns>
      /// True if the dependency was resolved and the parent record was
      /// successfully retried; otherwise, false.
      /// </returns>
      Task<bool> ResolveSqlDependencyAsync(
         string message,
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default,
         Func<Entity, SyncOptions, CancellationToken,
            Task<(bool Success, string? FailureMessage)>>?
            syncRecordFunc = null,
         Func<Entity, SyncOptions, CancellationToken,
            Task<bool>>?
            retryEntityFunc = null,
         Func<Entity, CancellationToken, Task<Guid?>>?
            findExistingFunc = null,
         ConcurrentDictionary<string, Guid>? idMappingCache = null
      );
   }
}
