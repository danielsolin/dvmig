using dvmig.Core.Interfaces;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Serilog;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Implementation of <see cref="ISyncErrorHandler"/> that orchestrates
    /// error handling strategies during entity synchronization.
    /// </summary>
    public class SyncErrorHandler : ISyncErrorHandler
    {
        private readonly IDataverseProvider _target;
        private readonly IDataPreservationManager _dataPreservation;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncErrorHandler"/> class.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="dataPreservation">The data preservation manager.</param>
        /// <param name="logger">The logger instance.</param>
        public SyncErrorHandler(
            IDataverseProvider target,
            IDataPreservationManager dataPreservation,
            ILogger logger)
        {
            _target = target;
            _dataPreservation = dataPreservation;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<(bool Success, string? FailureMessage)> HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct = default,
            Func<Entity, CancellationToken, Task>? updateFunc = null,
            Func<Entity, SyncOptions, IProgress<string>?, CancellationToken,
                Task<bool>>? statusTransitionFunc = null,
            Func<Exception, Entity, SyncOptions, IProgress<string>?, CancellationToken,
                Task<bool>>? resolveMissingDependencyFunc = null,
            Func<string, Entity, SyncOptions, IProgress<string>?, CancellationToken,
                Task<bool>>? resolveSqlDependencyFunc = null,
            Func<Exception, Entity, SyncOptions, IProgress<string>?, CancellationToken,
                Task<bool>>? stripAttributeFunc = null)
        {
            var msg = ex.Message.ToLower();

            if (msg.Contains("already exists") ||
                msg.Contains("duplicate currency record") ||
                msg.Contains("duplicate key"))
            {
                _logger.Information(
                    "{Key}:{Id} already exists on target. " +
                    "Attempting update to ensure all fields are set.",
                    entity.LogicalName,
                    entity.Id
                );

                try
                {
                    if (options.PreserveDates)
                    {
                        await _dataPreservation.PreserveDatesAsync(entity, ct);
                    }

                    // Try standard update first
                    if (updateFunc != null)
                    {
                        await updateFunc(entity, ct);
                    }
                    else
                    {
                        await _target.UpdateAsync(entity, ct);
                    }

                    return (true, string.Empty);
                }
                catch (Exception updateEx)
                {
                    var updateMsg = updateEx.Message.ToLower();

                    if (updateMsg.Contains("is not a valid status code") &&
                        statusTransitionFunc != null)
                    {
                        var success = await statusTransitionFunc(
                            entity,
                            options,
                            progress,
                            ct
                        );

                        return success
                            ? (true, string.Empty)
                            : (false, FormatFailureMessage(
                                "Status transition failed",
                                updateEx
                            ));
                    }

                    if ((updateMsg.Contains("conflicted with the foreign key constraint") ||
                         updateMsg.Contains("conflicted with a constraint")) &&
                        resolveSqlDependencyFunc != null)
                    {
                        var success = await resolveSqlDependencyFunc(
                            updateEx.Message,
                            entity,
                            options,
                            progress,
                            ct
                        );

                        return success
                            ? (true, string.Empty)
                            : (false, FormatFailureMessage(
                                "SQL dependency resolution failed",
                                updateEx
                            ));
                    }

                    _logger.Warning(
                        "Update failed for existing record {Key}:{Id}: {Msg}",
                        entity.LogicalName,
                        entity.Id,
                        updateEx.Message
                    );

                    return (false, FormatFailureMessage(
                        "Update failed for existing record",
                        updateEx
                    ));
                }
            }

            if (msg.Contains("is not a valid status code") &&
                statusTransitionFunc != null)
            {
                var success = await statusTransitionFunc(
                    entity,
                    options,
                    progress,
                    ct
                );

                return success
                    ? (true, string.Empty)
                    : (false, FormatFailureMessage(
                        "Status transition failed",
                        ex
                    ));
            }

            if (msg.Contains("does not exist") &&
                resolveMissingDependencyFunc != null)
            {
                var success = await resolveMissingDependencyFunc(
                    ex,
                    entity,
                    options,
                    progress,
                    ct
                );

                return success
                    ? (true, string.Empty)
                    : (false, FormatFailureMessage(
                        "Missing dependency resolution failed",
                        ex
                    ));
            }

            if (msg.Contains("conflicted with the foreign key constraint") &&
                resolveSqlDependencyFunc != null)
            {
                var success = await resolveSqlDependencyFunc(
                    ex.Message,
                    entity,
                    options,
                    progress,
                    ct
                );

                return success
                    ? (true, string.Empty)
                    : (false, FormatFailureMessage(
                        "SQL dependency resolution failed",
                        ex
                    ));
            }

            if ((msg.Contains("cannot be modified") ||
                 msg.Contains("cannot be set on creation") ||
                 msg.Contains("outside the valid range")) &&
                stripAttributeFunc != null)
            {
                var success = await stripAttributeFunc(
                    ex,
                    entity,
                    options,
                    progress,
                    ct
                );

                return success
                    ? (true, string.Empty)
                    : (false, FormatFailureMessage(
                        "Strip attribute retry failed",
                        ex
                    ));
            }

            _logger.Error(
                ex,
                "Unresolved error for {Key}:{Id}",
                entity.LogicalName,
                entity.Id
            );

            progress?.Report(
                $"FAILED {entity.LogicalName}:{entity.Id} - {ex.Message}"
            );

            return (false, FormatFailureMessage(
                "Unresolved error",
                ex
            ));
        }

        /// <inheritdoc />
        public string FormatFailureMessage(string context, Exception ex)
        {
            return $"{context}: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
