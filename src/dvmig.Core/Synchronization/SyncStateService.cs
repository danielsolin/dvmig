using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using dvmig.Core.Interfaces;

namespace dvmig.Core.Synchronization
{
   public class SyncStateService : ISyncStateService
   {
      private readonly ConcurrentDictionary<string, int> _recursionTracker =
         new ConcurrentDictionary<string, int>();

      private ConcurrentDictionary<Guid, byte> _syncedIds =
         new ConcurrentDictionary<Guid, byte>();

      public ConcurrentDictionary<string, HashSet<string>> TriedDependencies
         { get; } = new ConcurrentDictionary<string, HashSet<string>>();

      public ConcurrentDictionary<string, Guid> IdMappingCache
         { get; } = new ConcurrentDictionary<string, Guid>();

      public void InitializeSyncedIds(IEnumerable<Guid> ids)
      {
         _syncedIds = new ConcurrentDictionary<Guid, byte>(
            ids.Select(id => new KeyValuePair<Guid, byte>(id, 1))
         );
      }

      public bool IsSynced(Guid id)
      {
         return _syncedIds.ContainsKey(id);
      }

      public void MarkAsSynced(Guid id)
      {
         _syncedIds.TryAdd(id, 1);
      }

      public bool TryEnterRecordScope(string recordKey, int maxDepth)
      {
         var depth = _recursionTracker.AddOrUpdate(
            recordKey,
            1,
            (_, v) => v + 1
         );

         return depth <= maxDepth;
      }

      public void LeaveRecordScope(string recordKey)
      {
         _recursionTracker.AddOrUpdate(recordKey, 0, (_, v) => v - 1);
      }

      public void ClearState()
      {
         TriedDependencies.Clear();
         IdMappingCache.Clear();
         _recursionTracker.Clear();
      }
   }
}
