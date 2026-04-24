using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
    public partial class SyncEngine
    {
        /// <summary>
        /// Orchestrates the error handling strategy for exceptions encountered 
        /// during record synchronization. Analyzes the exception type and 
        /// applies specific resolution logic (e.g., duplicate handling, 
        /// missing dependencies).
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="entity">The entity record being synchronized.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the exception was resolved and synchronization was 
        /// successful; otherwise, false.
        /// </returns>
        private async Task<bool> HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
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
                    await _target.UpdateAsync(entity, ct);

                    return true;
                }
                catch (Exception updateEx)
                {
                    var updateMsg = updateEx.Message.ToLower();

                    // If update fails due to state/status, fall through to 
                    // the status handling logic.
                    if (updateMsg.Contains("is not a valid status code"))
                    {
                        return await HandleStatusTransitionAsync(
                            entity,
                            options,
                            progress,
                            ct
                        );
                    }

                    if (updateMsg.Contains(
                            "conflicted with the foreign key constraint") ||
                        updateMsg.Contains("conflicted with a constraint"))
                    {
                        return await ResolveSqlDependencyAsync(
                            updateEx.Message,
                            entity,
                            options,
                            progress,
                            ct
                        );
                    }

                    _logger.Warning(
                        "Update failed for existing record {Key}:{Id}: {Msg}",
                        entity.LogicalName,
                        entity.Id,
                        updateEx.Message
                    );

                    return false;
                }
            }

            if (msg.Contains("is not a valid status code"))
            {
                return await HandleStatusTransitionAsync(
                    entity, 
                    options, 
                    progress, 
                    ct
                );
            }

            if (msg.Contains("does not exist"))
            {
                return await ResolveMissingDependencyAsync(
                    ex,
                    entity,
                    options,
                    progress,
                    ct
                );
            }

            if (msg.Contains("conflicted with the foreign key constraint"))
            {
                return await ResolveSqlDependencyAsync(
                    ex.Message,
                    entity,
                    options,
                    progress,
                    ct
                );
            }

            if (msg.Contains("cannot be modified") ||
                msg.Contains("cannot be set on creation") ||
                msg.Contains("outside the valid range"))
            {
                return await StripAttributeAndRetryAsync(
                    ex,
                    entity,
                    options,
                    progress,
                    ct
                );
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

            return false;
        }

        /// <summary>
        /// Handles state and status transitions for entities. This method 
        /// strips the state/status codes to allow record creation/update and 
        /// then applies them using a SetState request or a subsequent update.
        /// </summary>
        /// <param name="entity">The entity record.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="progress">The progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the entity was successfully synchronized and its status 
        /// transition applied; otherwise, false.
        /// </returns>
        private async Task<bool> HandleStatusTransitionAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
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

            var success = await CreateWithFixStrategyAsync(
                entity,
                options,
                progress,
                ct
            );

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
        /// Attempts to resolve missing SQL-level dependencies (foreign key 
        /// constraints) by identifying the missing record, synchronizing it 
        /// to the target, and retrying the parent record.
        /// </summary>
        /// <param name="message">The SQL error message.</param>
        /// <param name="entity">The parent entity record.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the dependency was resolved and the parent record was 
        /// successfully retried; otherwise, false.
        /// </returns>
        private async Task<bool> ResolveSqlDependencyAsync(
            string message,
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct
        )
        {
            // Extract column name from message (e.g., column
            // 'TransactionCurrencyId')
            var match = Regex.Match(message, @"column '(\w+)'");
            if (!match.Success)
            {
                return false;
            }

            var columnName = match.Groups[1].Value.ToLower();

            // Find the attribute in the entity
            var attr = entity.Attributes
                .FirstOrDefault(a => a.Key.ToLower() == columnName);

            if (attr.Value is EntityReference er)
            {
                _logger.Information(
                    "Detected missing SQL dependency for {Attr} on {Entity}. " +
                    "Attempting to resolve {DepType}:{DepId}",
                    columnName,
                    entity.LogicalName,
                    er.LogicalName,
                    er.Id
                );

                progress?.Report(
                    $"Resolving SQL dependency: {er.LogicalName}:{er.Id}"
                );

                var missingRecord = await _source.RetrieveAsync(
                    er.LogicalName,
                    er.Id,
                    null,
                    ct
                );

                if (missingRecord != null)
                {
                    // Dynamic Mapping: Try to find by business key first
                    var targetId = await FindExistingOnTargetAsync(
                        missingRecord,
                        ct
                    );

                    if (targetId.HasValue)
                    {
                        _logger.Information(
                            "Found {Type} by business key. " +
                            "Mapping {SourceId} -> {TargetId}",
                            er.LogicalName,
                            er.Id,
                            targetId.Value
                        );

                        _idMappingCache[$"{er.LogicalName}:{er.Id}"] =
                            targetId.Value;

                        return await RetryEntityAsync(
                            entity, 
                            options, 
                            progress, 
                            ct
                        );
                    }

                    // Re-use the existing logic to sync the missing record
                    var success = await SyncRecordAsync(
                        missingRecord,
                        options,
                        progress,
                        ct
                    );

                    if (success)
                    {
                        return await RetryEntityAsync(
                            entity, 
                            options, 
                            progress, 
                            ct
                        );
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Prepares and retries the synchronization of an entity.
        /// </summary>
        /// <param name="entity">The entity record to retry.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the retry was successful; otherwise, false.
        /// </returns>
        private async Task<bool> RetryEntityAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var metadata = await GetMetadataAsync(entity.LogicalName, ct);
            var prepared = await PrepareEntityForTargetAsync(
                entity,
                metadata,
                options,
                ct
            );

            if (metadata?.IsIntersect == true)
            {
                return await SyncIntersectEntityAsync(
                    prepared,
                    options,
                    progress,
                    ct
                );
            }

            return await CreateWithFixStrategyAsync(
                prepared,
                options,
                progress,
                ct
            );
        }

        /// <summary>
        /// Identifies an attribute that caused a synchronization failure, 
        /// removes it from the entity, and retries the synchronization.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="entity">The entity record.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="progress">The progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the attribute was stripped and the retry was successful; 
        /// otherwise, false.
        /// </returns>
        private async Task<bool> StripAttributeAndRetryAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var match = Regex.Match(
                ex.Message,
                @"'(\w+)'"
            );

            if (match.Success)
            {
                var attrName = match.Groups[1].Value;
                if (entity.Attributes.Contains(attrName))
                {
                    _logger.Warning(
                        "Stripping attribute '{Attr}' for {Key}:{Id}",
                        attrName,
                        entity.LogicalName,
                        entity.Id
                    );

                    progress?.Report(
                        $"Stripping attribute '{attrName}' and retrying..."
                    );

                    entity.Attributes.Remove(attrName);

                    return await CreateWithFixStrategyAsync(
                        entity,
                        options,
                        progress,
                        ct
                    );
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve missing Dataverse dependencies (e.g., missing 
        /// lookup records) by identifying the missing record, synchronizing 
        /// it to the target, and retrying the parent record.
        /// </summary>
        /// <param name="ex">The exception indicating the missing record.</param>
        /// <param name="entity">The parent entity record.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the dependency was resolved and the parent record was 
        /// successfully retried; otherwise, false.
        /// </returns>
        private async Task<bool> ResolveMissingDependencyAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            // More robust regex to handle both:
            // "Account with Id=GUID does not exist"
            // "Entity 'transactioncurrency' With Id = GUID Does Not Exist"
            var pattern =
                @"(?:Entity )?'?(\w+)'? [Ww]ith Id\s*=\s*([a-fA-F0-9-]+)";

            var match = Regex.Match(
                ex.Message,
                pattern,
                RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                var missingType = match.Groups[1].Value.ToLower();
                var missingId = Guid.Parse(match.Groups[2].Value);
                var recordKey = $"{entity.LogicalName}:{entity.Id}";
                var dependencyKey = $"{missingType}:{missingId}";

                var tried = _triedDependencies.GetOrAdd(
                    recordKey,
                    _ => new HashSet<string>()
                );

                if (tried.Contains(dependencyKey))
                {
                    _logger.Warning(
                        "Already tried to resolve {Dep} for {Record}. " +
                        "Falling back to stripping logic.",
                        dependencyKey,
                        recordKey
                    );
                }
                else
                {
                    tried.Add(dependencyKey);

                    _logger.Information(
                        "Missing dependency {Type}:{Id} detected. " +
                        "Attempting to resolve.",
                        missingType,
                        missingId
                    );

                    progress?.Report(
                        $"Resolving missing dependency: {missingType}:{missingId}"
                    );

                    var missingRecord = await _source.RetrieveAsync(
                        missingType,
                        missingId,
                        null,
                        ct
                    );

                    if (missingRecord != null)
                    {
                        // Dynamic Mapping: Try to find by business key first
                        var targetId = await FindExistingOnTargetAsync(
                            missingRecord,
                            ct
                        );

                        if (targetId.HasValue)
                        {
                            _logger.Information(
                                "Found {Type} by business key. " +
                                "Mapping {SourceId} -> {TargetId}",
                                missingType,
                                missingId,
                                targetId.Value
                            );

                            _idMappingCache[dependencyKey] = targetId.Value;

                            return await RetryEntityAsync(
                                entity, 
                                options, 
                                progress, 
                                ct
                            );
                        }

                        // Normal Sync: Try to sync the record over
                        var success = await SyncRecordAsync(
                            missingRecord,
                            options,
                            progress,
                            ct
                        );

                        if (success)
                        {
                            return await RetryEntityAsync(
                                entity, 
                                options, 
                                progress, 
                                ct
                            );
                        }
                    }
                }

                // Fallback: If auto-sync of dependency failed 
                // (or not found),
                // strip the attribute and retry the parent record.
                if (options.StripMissingDependencies)
                {
                    var attrToStrip = entity.Attributes
                        .FirstOrDefault(a =>
                            a.Value is EntityReference er &&
                            er.LogicalName == missingType &&
                            er.Id == missingId
                        ).Key;

                    if (!string.IsNullOrEmpty(attrToStrip))
                    {
                        _logger.Warning(
                            "Dependency resolution failed for {Type}:{Id}. " +
                            "Stripping attribute '{Attr}' from " +
                            "{ParentType}:{ParentId} and retrying.",
                            missingType,
                            missingId,
                            attrToStrip,
                            entity.LogicalName,
                            entity.Id
                        );

                        progress?.Report(
                            $"Dependency resolution failed. Stripping '{attrToStrip}' and retrying..."
                        );

                        entity.Attributes.Remove(attrToStrip);

                        return await CreateWithFixStrategyAsync(
                            entity,
                            options,
                            progress,
                            ct
                        );
                    }
                }
            }

            return false;
        }
    }
}
