using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using Microsoft.Xrm.Sdk;
using Serilog;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Implementation of <see cref="IStatusTransitionHandler"/> that handles
    /// state and status transitions during entity synchronization.
    /// </summary>
    public class StatusTransitionHandler : IStatusTransitionHandler
    {
        private readonly IDataverseProvider _target;
        private readonly ISetupService _setupService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="StatusTransitionHandler"/> class.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="setupService">The setup service.</param>
        /// <param name="logger">The logger instance.</param>
        public StatusTransitionHandler(
            IDataverseProvider target,
            ISetupService setupService,
            ILogger logger)
        {
            _target = target;
            _setupService = setupService;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> HandleStatusTransitionAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct = default,
            Func<Entity, SyncOptions, IProgress<string>?, CancellationToken,
                Task<(bool Success, string? FailureMessage)>>? createOrUpdateFunc = null)
        {
            var recordKey = $"{entity.LogicalName}:{entity.Id}";
            _logger.Information(
                "Handling state/status transition for {Key}",
                recordKey
            );

            var stateValue = entity.Contains("statecode")
                ? entity["statecode"]
                : null;
            var statusValue = entity.Contains("statuscode")
                ? entity["statuscode"]
                : null;

            _logger.Debug(
                "Transition for {Key} - State: {State}, Status: {Status}",
                recordKey,
                stateValue ?? "NULL",
                statusValue ?? "NULL"
            );

            // Remove status/state to allow basic creation/update
            entity.Attributes.Remove("statecode");
            entity.Attributes.Remove("statuscode");

            // Use the provided function or default to basic create
            var (success, _) = createOrUpdateFunc != null
                ? await createOrUpdateFunc(entity, options, progress, ct)
                : await BasicCreateAsync(entity, ct);

            if (success && (stateValue != null || statusValue != null))
            {
                try
                {
                    // Ensure we have OptionSetValue objects
                    var stateOsv = ToOptionSetValue(stateValue);
                    var statusOsv = ToOptionSetValue(statusValue);

                    if (stateOsv != null)
                    {
                        _logger.Information(
                            "Applying SetState for {Key} (State: {State})",
                            recordKey,
                            stateOsv.Value
                        );

                        var stateReq = new OrganizationRequest("SetState");
                        stateReq.Parameters["EntityMoniker"] =
                            new EntityReference(
                                entity.LogicalName,
                                entity.Id
                            );
                        stateReq.Parameters["State"] = stateOsv;
                        stateReq.Parameters["Status"] =
                            statusOsv ?? new OptionSetValue(-1);

                        await _target.ExecuteAsync(stateReq, ct);
                    }
                    else
                    {
                        _logger.Warning(
                            "Cannot apply SetState for {Key}: State is null. " +
                            "Trying fallback Update for status only.",
                            recordKey
                        );

                        // Throw to trigger the fallback logic in catch block
                        throw new InvalidOperationException("State is null");
                    }
                }
                catch (Exception stateEx)
                {
                    _logger.Warning(
                        "SetState failed for {Key}: {Msg}",
                        recordKey,
                        stateEx.Message
                    );

                    // Fallback: Modern Update with only state/status
                    try
                    {
                        var transitionUpdate = new Entity(
                            entity.LogicalName,
                            entity.Id
                        );

                        if (stateValue != null)
                        {
                            transitionUpdate["statecode"] = stateValue;
                        }

                        if (statusValue != null)
                        {
                            transitionUpdate["statuscode"] = statusValue;
                        }

                        await _target.UpdateAsync(transitionUpdate, ct);
                        _logger.Information(
                            "Applied transition via fallback Update for {Key}",
                            recordKey
                        );
                    }
                    catch (Exception finalEx)
                    {
                        _logger.Warning(
                            "All transition attempts failed for {Key}: {Msg}",
                            recordKey,
                            finalEx.Message
                        );
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// Converts a raw value to an <see cref="OptionSetValue"/>.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>
        /// An <see cref="OptionSetValue"/> if the conversion is possible;
        /// otherwise, null.
        /// </returns>
        private OptionSetValue? ToOptionSetValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is OptionSetValue osv)
            {
                return osv;
            }

            if (value is int i)
            {
                return new OptionSetValue(i);
            }

            return null;
        }

        /// <summary>
        /// Performs a basic create operation for the entity.
        /// </summary>
        /// <param name="entity">The entity to create.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A tuple indicating success and any failure message.</returns>
        private async Task<(bool Success, string? FailureMessage)> BasicCreateAsync(
            Entity entity,
            CancellationToken ct)
        {
            try
            {
                await _target.CreateAsync(entity, ct);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Basic create failed: {ex.Message}");
            }
        }
    }
}
