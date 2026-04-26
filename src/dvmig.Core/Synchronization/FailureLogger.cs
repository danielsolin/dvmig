using dvmig.Providers;
using dvmig.Shared.Metadata;
using Microsoft.Xrm.Sdk;
using Serilog;

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
                    SchemaConstants.MigrationFailure.EntityLogicalName
                );

                var failureName = $"{entity.LogicalName}:{entity.Id}";
                failure[SchemaConstants.MigrationFailure.Name] =
                    failureName.Length <= 100
                        ? failureName
                        : failureName.Substring(0, 100);

                failure[SchemaConstants.MigrationFailure.SourceId] =
                    entity.Id.ToString();

                failure[SchemaConstants.MigrationFailure.EntityLogicalNameAttr] =
                    entity.LogicalName;

                failure[SchemaConstants.MigrationFailure.ErrorMessage] =
                    errorMessage;

                failure[SchemaConstants.MigrationFailure.Timestamp] =
                    DateTime.UtcNow;

                await _target.CreateAsync(failure, ct);
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