using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IFailureService"/> that logs and manages
   /// migration failures in the target Dataverse environment.
   /// </summary>
   public class FailureService : IFailureService
   {
      private readonly IDataverseProvider _target;
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="FailureService"/> class.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="logger">The logger instance.</param>
      public FailureService(
         IDataverseProvider target,
         ILogger logger
      )
      {
         _target = target;
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task LogFailureToTargetAsync(
         Entity entity,
         string errorMessage,
         CancellationToken ct = default
      )
      {
         try
         {
            var failure = new Entity(
               SystemConstants.MigrationFailure.EntityLogicalName
            );

            var failureName = $"{entity.LogicalName}:{entity.Id}";
            failure[SystemConstants.MigrationFailure.Name] =
               failureName.Length <= 100
                  ? failureName
                  : failureName.Substring(0, 100);

            failure[SystemConstants.MigrationFailure.SourceId] =
               entity.Id.ToString();

            var logicalNameAttr =
               SystemConstants.MigrationFailure.EntityLogicalNameAttr;

            failure[logicalNameAttr] = entity.LogicalName;

            failure[SystemConstants.MigrationFailure.ErrorMessage] =
               errorMessage;

            failure[SystemConstants.MigrationFailure.Timestamp] =
               DateTime.UtcNow;

            await _target.CreateAsync(failure, ct);
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            var message =
               "Failed to log migration failure for {Entity}:{Id} " +
               "to target Dataverse.";

            _logger.Error(
               ex,
               message,
               entity.LogicalName,
               entity.Id
            );

            throw new InvalidOperationException(
               message,
               ex
            );
         }
      }

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
         string? entityLogicalName = null,
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

         if (!string.IsNullOrEmpty(entityLogicalName))
         {
            query.Criteria.AddCondition(
               SystemConstants.MigrationFailure.EntityLogicalNameAttr,
               ConditionOperator.Equal,
               entityLogicalName
            );
         }

         query.AddOrder(
            SystemConstants.MigrationFailure.Timestamp,
            OrderType.Ascending
         );

         var result = await target.RetrieveMultipleAsync(query, ct);

         return result.Entities.Select(e => new MigrationFailureRecord
         {
            Id = e.Id,
            EntityLogicalName = e.GetAttributeValue<string>(
               SystemConstants.MigrationFailure.EntityLogicalNameAttr
            ) ?? SystemConstants.MigrationFailure.NotAvailable,
            SourceId = e.GetAttributeValue<string>(
               SystemConstants.MigrationFailure.SourceId
            ) ?? SystemConstants.MigrationFailure.NotAvailable,
            ErrorMessage = e.GetAttributeValue<string>(
               SystemConstants.MigrationFailure.ErrorMessage
            ) ?? SystemConstants.MigrationFailure.NotAvailable,
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

      /// <inheritdoc />
      public async Task DeleteFailureAsync(
         IDataverseProvider target,
         Guid failureId,
         CancellationToken ct = default
      )
      {
         await target.DeleteAsync(
            SystemConstants.MigrationFailure.EntityLogicalName,
            failureId,
            ct
         );
      }
   }
}
