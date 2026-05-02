using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IStatusService"/> that handles
   /// state and status transitions during entity synchronization.
   /// </summary>
   public class StatusService : IStatusService
   {
      private readonly IDataverseProvider _target;
      private readonly ISourceDateService _sourceDateService;
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="StatusService"/> class.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="sourceDateService">The source date service.</param>
      /// <param name="logger">The logger instance.</param>
      public StatusService(
         IDataverseProvider target,
         ISourceDateService sourceDateService,
         ILogger logger
      )
      {
         _target = target;
         _sourceDateService = sourceDateService;
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task<bool> HandleStatusTransitionAsync(
         Entity entity,
         SyncOptions options,
         CancellationToken ct = default,
         Func<Entity, SyncOptions, CancellationToken,
            Task<(bool Success, string? FailureMessage)>>?
            createOrUpdateFunc = null
      )
      {
         var recordKey = $"{entity.LogicalName}:{entity.Id}";

         _logger.Information(
            "Handling state/status transition for {Key}",
            recordKey
         );

         var stateValue = entity.Contains(
            SystemConstants.DataverseAttributes.StateCode)
            ? entity[SystemConstants.DataverseAttributes.StateCode]
            : null;

         var statusValue = entity.Contains(
            SystemConstants.DataverseAttributes.StatusCode)
            ? entity[SystemConstants.DataverseAttributes.StatusCode]
            : null;

         _logger.Debug(
            "Transition for {Key} - State: {State}, Status: {Status}",
            recordKey,
            stateValue ?? "NULL",
            statusValue ?? "NULL"
         );

         entity.Attributes.Remove(
            SystemConstants.DataverseAttributes.StateCode);
         entity.Attributes.Remove(
            SystemConstants.DataverseAttributes.StatusCode);

         var (success, _) = createOrUpdateFunc != null
            ? await createOrUpdateFunc(entity, options, ct)
            : await BasicCreateAsync(entity, ct);

         if (success && (stateValue != null || statusValue != null))
         {
            try
            {
               var stateOsv = ToOptionSetValue(stateValue);
               var statusOsv = ToOptionSetValue(statusValue);

               if (stateOsv != null)
               {
                  _logger.Information(
                     "Applying SetState for {Key} (State: {State})",
                     recordKey,
                     stateOsv.Value
                  );

                  var request = new SetStateRequest
                  {
                     EntityMoniker = entity.ToEntityReference(),
                     State = stateOsv,
                     Status = statusOsv ?? new OptionSetValue(-1)
                  };

                  await _target.ExecuteAsync(request, ct);
               }
               else
               {
                  _logger.Warning(
                     "Cannot apply SetState for {Key}: State is null. " +
                     "Trying fallback Update for status only.",
                     recordKey
                  );

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

               try
               {
                  var transitionUpdate = new Entity(
                     entity.LogicalName,
                     entity.Id
                  );

                  if (stateValue != null)
                     transitionUpdate[
                        SystemConstants.DataverseAttributes.StateCode
                     ] = stateValue;

                  if (statusValue != null)
                     transitionUpdate[
                        SystemConstants.DataverseAttributes.StatusCode
                     ] = statusValue;

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

      private OptionSetValue? ToOptionSetValue(object? value)
      {
         if (value == null)
            return null;

         if (value is OptionSetValue osv)
            return osv;

         if (value is int i)
            return new OptionSetValue(i);

         return null;
      }

      private async Task<(bool Success, string? FailureMessage)>
         BasicCreateAsync(
            Entity entity,
            CancellationToken ct
         )
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
