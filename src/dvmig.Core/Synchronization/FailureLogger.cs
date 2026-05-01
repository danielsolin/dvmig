using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IFailureLogger"/> that logs migration
   /// failures to the target Dataverse environment.
   /// </summary>
   public class FailureLogger : IFailureLogger
   {
      private readonly IDataverseProvider _target;
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="FailureLogger"/> class.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="logger">The logger instance.</param>
      public FailureLogger(
          IDataverseProvider target,
          ILogger logger)
      {
         _target = target;
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task LogFailureToTargetAsync(
          Entity entity,
          string errorMessage,
          CancellationToken ct = default)
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
   }
}
