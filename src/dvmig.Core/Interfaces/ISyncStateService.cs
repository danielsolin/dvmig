using System.Collections.Concurrent;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Manages state during the synchronization process, such as recursion tracking, 
   /// synced record IDs, and dependency tracking.
   /// </summary>
   public interface ISyncStateService
   {
      ConcurrentDictionary<string, HashSet<string>> TriedDependencies { get; }
      ConcurrentDictionary<string, Guid> IdMappingCache { get; }

      void InitializeSyncedIds(IEnumerable<Guid> ids);
      bool IsSynced(Guid id);
      void MarkAsSynced(Guid id);

      bool TryEnterRecordScope(string recordKey, int maxDepth);
      void LeaveRecordScope(string recordKey);

      void ClearState();
   }
}
