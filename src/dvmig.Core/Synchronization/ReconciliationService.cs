using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Implementation of the reconciliation service.
    /// </summary>
    public class ReconciliationService : IReconciliationService
    {
        /// <inheritdoc />
        public async Task<bool> IsInitializedAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        )
        {
            var meta = await target.GetEntityMetadataAsync(
                SystemConstants.MigrationFailure.EntityLogicalName,
                ct
            );

            return meta != null;
        }

        /// <inheritdoc />
        public async Task<List<MigrationFailureRecord>> GetFailuresAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        )
        {
            var query = new QueryExpression(
                SystemConstants.MigrationFailure.EntityLogicalName
            )
            {
                ColumnSet = new ColumnSet(
                    SystemConstants.MigrationFailure.SourceId,
                    SystemConstants.MigrationFailure.EntityLogicalNameAttr,
                    SystemConstants.MigrationFailure.ErrorMessage,
                    SystemConstants.MigrationFailure.Timestamp
                )
            };

            query.AddOrder(
                SystemConstants.MigrationFailure.Timestamp,
                OrderType.Descending
            );

            var result = await target.RetrieveMultipleAsync(query, ct);

            return result.Entities.Select(e => new MigrationFailureRecord
            {
                Id = e.Id,
                EntityLogicalName = e.GetAttributeValue<string>(
                    SystemConstants.MigrationFailure.EntityLogicalNameAttr
                ) ?? "N/A",
                SourceId = e.GetAttributeValue<string>(
                    SystemConstants.MigrationFailure.SourceId
                ) ?? "N/A",
                ErrorMessage = e.GetAttributeValue<string>(
                    SystemConstants.MigrationFailure.ErrorMessage
                ) ?? "N/A",
                TimestampUtc = e.GetAttributeValue<DateTime>(
                    SystemConstants.MigrationFailure.Timestamp
                )
            }).ToList();
        }

        /// <inheritdoc />
        public async Task ClearFailuresAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        )
        {
            var query = new QueryExpression(
                SystemConstants.MigrationFailure.EntityLogicalName
            )
            {
                ColumnSet = new ColumnSet(false)
            };

            var result = await target.RetrieveMultipleAsync(query, ct);

            foreach (var entity in result.Entities)
            {
                ct.ThrowIfCancellationRequested();

                await target.DeleteAsync(
                    SystemConstants.MigrationFailure.EntityLogicalName,
                    entity.Id,
                    ct
                );
            }
        }
    }
}
