using Microsoft.Xrm.Sdk;

namespace dvmig.Core
{
    public interface IDataPreservationManager
    {
        Task PreserveDatesAsync(
            Entity sourceEntity,
            CancellationToken ct = default
        );

        Task DeleteSourceDateAsync(
            string logicalName,
            Guid entityId,
            CancellationToken ct = default
        );
    }
}
