using dvmig.Core.Providers;
using dvmig.Shared.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Represents a recorded migration failure.
    /// </summary>
    public class MigrationFailureRecord
    {
        public Guid Id { get; set; }
        public string EntityLogicalName { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>
    /// Defines the contract for a service that retrieves and manages 
    /// migration failure records.
    /// </summary>
    public interface IReconciliationService
    {
        /// <summary>
        /// Checks if the failure logging entity is installed on the target.
        /// </summary>
        Task<bool> IsInitializedAsync(IDataverseProvider target, CancellationToken ct = default);

        /// <summary>
        /// Retrieves all recorded migration failures from the target.
        /// </summary>
        Task<List<MigrationFailureRecord>> GetFailuresAsync(IDataverseProvider target, CancellationToken ct = default);

        /// <summary>
        /// Clears all recorded migration failures from the target.
        /// </summary>
        Task ClearFailuresAsync(IDataverseProvider target, CancellationToken ct = default);
    }

    /// <summary>
    /// Implementation of the reconciliation service.
    /// </summary>
    public class ReconciliationService : IReconciliationService
    {
        /// <inheritdoc />
        public async Task<bool> IsInitializedAsync(IDataverseProvider target, CancellationToken ct = default)
        {
            var meta = await target.GetEntityMetadataAsync(
                SchemaConstants.MigrationFailure.EntityLogicalName,
                ct
            );

            return meta != null;
        }

        /// <inheritdoc />
        public async Task<List<MigrationFailureRecord>> GetFailuresAsync(IDataverseProvider target, CancellationToken ct = default)
        {
            var query = new QueryExpression(SchemaConstants.MigrationFailure.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(
                    SchemaConstants.MigrationFailure.SourceId,
                    SchemaConstants.MigrationFailure.EntityLogicalNameAttr,
                    SchemaConstants.MigrationFailure.ErrorMessage,
                    SchemaConstants.MigrationFailure.Timestamp
                )
            };
            query.AddOrder(SchemaConstants.MigrationFailure.Timestamp, OrderType.Descending);

            var result = await target.RetrieveMultipleAsync(query, ct);

            return result.Entities.Select(e => new MigrationFailureRecord
            {
                Id = e.Id,
                EntityLogicalName = e.GetAttributeValue<string>(SchemaConstants.MigrationFailure.EntityLogicalNameAttr) ?? "N/A",
                SourceId = e.GetAttributeValue<string>(SchemaConstants.MigrationFailure.SourceId) ?? "N/A",
                ErrorMessage = e.GetAttributeValue<string>(SchemaConstants.MigrationFailure.ErrorMessage) ?? "N/A",
                TimestampUtc = e.GetAttributeValue<DateTime>(SchemaConstants.MigrationFailure.Timestamp)
            }).ToList();
        }

        /// <inheritdoc />
        public async Task ClearFailuresAsync(IDataverseProvider target, CancellationToken ct = default)
        {
            var query = new QueryExpression(SchemaConstants.MigrationFailure.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };

            var result = await target.RetrieveMultipleAsync(query, ct);

            foreach (var entity in result.Entities)
            {
                ct.ThrowIfCancellationRequested();
                await target.DeleteAsync(SchemaConstants.MigrationFailure.EntityLogicalName, entity.Id, ct);
            }
        }
    }
}
