using Microsoft.Xrm.Sdk;

namespace dvmig.Core
{
    public interface ISyncEngine
    {
        Task SyncAsync(
            IEnumerable<Entity> entities,
            SyncOptions options,
            CancellationToken cancellationToken = default);

        Task<bool> SyncRecordAsync(
            Entity entity,
            SyncOptions options,
            CancellationToken cancellationToken = default);
    }

    public class SyncOptions
    {
        public bool SkipExisting { get; set; } = true;
        public int MaxDegreeOfParallelism { get; set; } = 1;
        public bool PreserveDates { get; set; } = true;
        public bool OverrideCreatedBy { get; set; } = true;
    }
}
