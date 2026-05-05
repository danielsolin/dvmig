using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Polly;
using Polly.Retry;

namespace dvmig.Core.Synchronization
{
   public class SyncResilienceService : ISyncResilienceService
   {
      private readonly IDataverseProvider _source;
      private readonly IDataverseProvider _target;
      private readonly ISyncStateService _state;
      private readonly ILogger _logger;

      private ISyncEngine? _engine;

      public SyncResilienceService(
         IDataverseProvider source,
         IDataverseProvider target,
         ISyncStateService state,
         ILogger logger
      )
      {
         _source = source;
         _target = target;
         _state = state;
         _logger = logger;
      }

      public void SetEngine(ISyncEngine engine)
      {
         _engine = engine;
      }

      #region Error Handling (formerly ErrorService)

      public async Task<(bool Success, string? FailureMessage)>
         HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            Entity sourceEntity,
            SyncOptions options,
            Guid? creatorId = null,
            Guid? modifiedById = null,
            CancellationToken ct = default
         )
      {
         var msg = ex.Message.ToLower();

         bool isDuplicate =
            msg.Contains(SystemConstants.ErrorKeywords.AlreadyExists) ||
            msg.Contains(SystemConstants.ErrorKeywords.DuplicateCurrency) ||
            msg.Contains(SystemConstants.ErrorKeywords.DuplicateKey);

         if (isDuplicate)
            return await HandleDuplicateAsync(entity, modifiedById, ct);

         if (msg.Contains(SystemConstants.ErrorKeywords.InvalidStatusCode))
         {
            var success = await HandleStatusTransitionAsync(
               entity,
               sourceEntity,
               options,
               ct,
               modifiedById
            );

            if (success)
               return (true, string.Empty);

            return (false, FormatFailureMessage("Status transition failed", ex));
         }

         if (msg.Contains(SystemConstants.ErrorKeywords.DoesNotExist) ||
             msg.Contains(SystemConstants.ErrorKeywords.ForeignKeyConflict))
         {
            var success = await ResolveDependencyAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage("Dependency resolution failed", ex)
            );
         }

         if (msg.Contains(SystemConstants.ErrorKeywords.CannotBeModified) ||
             msg.Contains(SystemConstants.ErrorKeywords.CannotBeSetOnCreation) ||
             msg.Contains(SystemConstants.ErrorKeywords.OutsideValidRange))
         {
            var success = await StripAttributeAndRetryAsync(
               ex,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (false, FormatFailureMessage("Attribute stripping failed", ex));
         }

         _logger.Error(
            ex,
            "Unresolved error for {Key}:{Id}",
            entity.LogicalName,
            entity.Id
         );

         return (false, FormatFailureMessage("Unresolved error", ex));
      }

      private async Task<(bool Success, string? FailureMessage)>
         HandleDuplicateAsync(
            Entity entity,
            Guid? modifiedById,
            CancellationToken ct
         )
      {
         _logger.Information(
            "{Key}:{Id} already exists. Attempting update.",
            entity.LogicalName,
            entity.Id
         );

         try
         {
            if (_engine != null)
            {
               var targetId = await _engine.FindExistingOnTargetAsync(entity, ct);

               if (targetId.HasValue && targetId.Value != entity.Id)
               {
                  entity.Id = targetId.Value;

                  var pk = await _target.GetPrimaryIdAttributeAsync(
                     entity.LogicalName,
                     ct
                  ) ?? $"{entity.LogicalName}id";

                  if (entity.Attributes.Contains(pk))
                     entity[pk] = targetId.Value;
               }
            }

            await _target.UpdateAsync(entity, ct, modifiedById);

            return (true, string.Empty);
         }
         catch (Exception updateEx)
         {
            _logger.Warning(
               "Update failed for existing record {Key}:{Id}: {Msg}.",
               entity.LogicalName,
               entity.Id,
               updateEx.Message
            );

            return (true, string.Empty);
         }
      }

      public string FormatFailureMessage(string context, Exception ex)
      {
         return $"{context}: {ex.GetType().Name}: {ex.Message}";
      }

      #endregion

      #region Status Transitions (formerly StatusService)

      public async Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         var stateValue = entity.Contains(
            SystemConstants.DataverseAttributes.StateCode
         )
            ? entity[SystemConstants.DataverseAttributes.StateCode]
            : null;

         var statusValue = entity.Contains(
            SystemConstants.DataverseAttributes.StatusCode
         )
            ? entity[SystemConstants.DataverseAttributes.StatusCode]
            : null;

         entity.Attributes.Remove(SystemConstants.DataverseAttributes.StateCode);
         entity.Attributes.Remove(SystemConstants.DataverseAttributes.StatusCode);

         sourceEntity.Attributes.Remove(
            SystemConstants.DataverseAttributes.StateCode
         );
         sourceEntity.Attributes.Remove(
            SystemConstants.DataverseAttributes.StatusCode
         );

         var (success, _) = _engine != null
            ? await _engine.SyncRecordAsync(sourceEntity, options, ct)
            : (false, "Sync engine missing");

         if (success && (stateValue != null || statusValue != null))
         {
            try
               {
               var stateOsv = ToOptionSetValue(stateValue);
               var statusOsv = ToOptionSetValue(statusValue);

               if (stateOsv != null)
               {
                  var request = new SetStateRequest
                  {
                     EntityMoniker = entity.ToEntityReference(),
                     State = stateOsv,
                     Status = statusOsv ?? new OptionSetValue(-1)
                  };

                  await _target.ExecuteAsync(request, ct, callerId);
               }
            }
            catch (Exception ex)
            {
               _logger.Warning(
                  "SetState failed for {Key}:{Id}: {Msg}. Trying fallback Update.",
                  entity.LogicalName,
                  entity.Id,
                  ex.Message
               );

               try
               {
                  var fallback = new Entity(entity.LogicalName, entity.Id);

                  if (stateValue != null)
                     fallback[SystemConstants.DataverseAttributes.StateCode] =
                        stateValue;

                  if (statusValue != null)
                     fallback[SystemConstants.DataverseAttributes.StatusCode] =
                        statusValue;

                  await _target.UpdateAsync(fallback, ct, callerId);
               }
               catch
               {
                  // Final failure ignored for status fallback
               }
            }
         }

         return success;
      }

      private OptionSetValue? ToOptionSetValue(object? value)
      {
         if (value == null)
            return null;

         return value is OptionSetValue osv ? osv : new OptionSetValue((int)value);
      }

      #endregion

      #region Dependency Resolution (formerly DependencyResolver)

      public async Task<bool> ResolveDependencyAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId = null,
         Guid? modifiedById = null,
         CancellationToken ct = default
      )
      {
         // Handle SQL Foreign Key Conflict
         if (ex.Message.Contains(SystemConstants.ErrorKeywords.ForeignKeyConflict))
         {
            return await ResolveSqlDependencyInternalAsync(
               ex.Message,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );
         }

         // Handle Missing Record
         var pattern = @"(?:Entity )?'?(\w+)'? [Ww]ith Id\s*=\s*([a-fA-F0-9-]+)";
         var match = Regex.Match(ex.Message, pattern, RegexOptions.IgnoreCase);

         if (!match.Success)
            return false;

         var type = match.Groups[1].Value.ToLower();
         var id = Guid.Parse(match.Groups[2].Value);

         return await ResolveDependencyInternalAsync(
            type,
            id,
            entity,
            sourceEntity,
            options,
            creatorId,
            modifiedById,
            ct
         );
      }

      private async Task<bool> ResolveSqlDependencyInternalAsync(
         string message,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CancellationToken ct
      )
      {
         var match = Regex.Match(message, @"column '(\w+)'");

         if (!match.Success)
            return false;

         var columnName = match.Groups[1].Value.ToLower();
         var attr = entity.Attributes
            .FirstOrDefault(a => a.Key.ToLower() == columnName);

         if (attr.Value is EntityReference er)
         {
            return await ResolveDependencyInternalAsync(
               er.LogicalName,
               er.Id,
               entity,
               sourceEntity,
               options,
               creatorId,
               modifiedById,
               ct
            );
         }

         return false;
      }

      private async Task<bool> ResolveDependencyInternalAsync(
         string type,
         Guid id,
         Entity parent,
         Entity sourceParent,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CancellationToken ct
      )
      {
         var parentKey = EntityHelper.GetRecordKey(parent);
         var depKey = EntityHelper.GetRecordKey(type, id);

         var tried = _state.TriedDependencies.GetOrAdd(
            parentKey,
            _ => new HashSet<string>()
         );

         if (tried.Contains(depKey))
         {
            if (options.StripMissingDependencies)
            {
               return await StripSpecificAttributeAsync(
                  type,
                  id,
                  parent,
                  sourceParent,
                  options,
                  creatorId,
                  modifiedById,
                  ct
               );
            }

            return false;
         }

         tried.Add(depKey);

         _logger.Information("Resolving missing dependency: {0}", depKey);

         var record = await _source.RetrieveAsync(type, id, null, ct);

         if (record != null && _engine != null)
         {
            var (success, _) = await _engine.SyncRecordAsync(record, options, ct);

            if (success)
               return await RetrySyncAsync(sourceParent, options, ct);
         }

         if (options.StripMissingDependencies)
         {
            return await StripSpecificAttributeAsync(
               type,
               id,
               parent,
               sourceParent,
               options,
               creatorId,
               modifiedById,
               ct
            );
         }

         return false;
      }

      private async Task<bool> StripSpecificAttributeAsync(
         string type,
         Guid id,
         Entity parent,
         Entity sourceParent,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CancellationToken ct
      )
      {
         var attr = parent.Attributes
            .FirstOrDefault(a =>
               a.Value is EntityReference er &&
               er.LogicalName == type &&
               er.Id == id
            ).Key;

         if (string.IsNullOrEmpty(attr))
            return false;

         _logger.Warning(
            "Stripping missing dependency '{0}' from {1}",
            attr,
            parent.LogicalName
         );

         parent.Attributes.Remove(attr);
         sourceParent.Attributes.Remove(attr);

         return await RetrySyncAsync(sourceParent, options, ct);
      }

      private async Task<bool> StripAttributeAndRetryAsync(
         Exception ex,
         Entity entity,
         Entity sourceEntity,
         SyncOptions options,
         Guid? creatorId,
         Guid? modifiedById,
         CancellationToken ct
      )
      {
         var match = Regex.Match(ex.Message, @"'(\w+)'");

         if (match.Success)
         {
            var attr = match.Groups[1].Value;

            if (entity.Attributes.Contains(attr))
            {
               _logger.Warning("Stripping problematic attribute '{0}'", attr);

               entity.Attributes.Remove(attr);
               sourceEntity.Attributes.Remove(attr);

               return await RetrySyncAsync(sourceEntity, options, ct);
            }
         }

         return false;
      }

      private async Task<bool> RetrySyncAsync(
         Entity entity,
         SyncOptions options,
         CancellationToken ct
      )
      {
         if (_engine == null)
            return false;

         var (success, _) = await _engine.SyncRecordAsync(entity, options, ct);

         return success;
      }

      #endregion

      #region Retries (formerly RetryService)

      public bool IsTransientError(Exception ex)
      {
         if (ex == null)
            return false;

         var msg = ex.Message.ToLower();

         bool isTransient =
            msg.Contains(SystemConstants.ErrorCodes.ServiceProtectionLimit) ||
            msg.Contains(SystemConstants.ErrorCodes.ConnectionTimeout) ||
            msg.Contains(SystemConstants.ErrorKeywords.TooManyRequests) ||
            msg.Contains("exceeded the limit") ||
            msg.Contains(SystemConstants.ErrorKeywords.CombinedExecutionTime) ||
            msg.Contains(SystemConstants.ErrorKeywords.GenericSqlError) ||
            msg.Contains(SystemConstants.ErrorKeywords.Timeout);

         if (isTransient)
            return true;

         return ex.InnerException != null && IsTransientError(ex.InnerException);
      }

      public AsyncRetryPolicy CreateRetryPolicy(int maxRetries = 5)
      {
         return Policy
            .Handle<Exception>(IsTransientError)
            .WaitAndRetryAsync(
               maxRetries,
               attempt => GetRetryDelay(attempt),
               (ex, time, count, ctx) =>
               {
                  _logger.Warning(
                     ex,
                     "Transient error. Retry {0} in {1:F0}s",
                     count,
                     time.TotalSeconds
                  );
               }
            );
      }

      private TimeSpan GetRetryDelay(int attempt)
      {
         return TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt + 2), 30));
      }

      #endregion
   }
}
