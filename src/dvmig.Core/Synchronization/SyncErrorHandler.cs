using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="ISyncErrorHandler"/> that orchestrates
   /// error handling strategies during entity synchronization.
   /// </summary>
   public class SyncErrorHandler : ISyncErrorHandler
   {
      private readonly IDataverseProvider _target;
      private readonly ISourceDateService _sourceDateService;
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="SyncErrorHandler"/> class.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="sourceDateService">The source date service.</param>
      /// <param name="logger">The logger instance.</param>
      public SyncErrorHandler(
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
      public async Task<(bool Success, string? FailureMessage)>
         HandleSyncExceptionAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            CancellationToken ct = default,
            Func<Entity, CancellationToken, Task>? updateFunc = null,
            Func<Entity, SyncOptions,
               CancellationToken, Task<bool>>?
               statusTransitionFunc = null,
            Func<Exception, Entity, SyncOptions,
               CancellationToken, Task<bool>>?
               resolveMissingDependencyFunc = null,
            Func<string, Entity, SyncOptions,
               CancellationToken, Task<bool>>?
               resolveSqlDependencyFunc = null,
            Func<Exception, Entity, SyncOptions,
               CancellationToken, Task<bool>>?
               stripAttributeFunc = null,
            Func<Entity, CancellationToken, Task<Guid?>>?
               findExistingFunc = null
         )
      {
         var msg = ex.Message.ToLower();

         bool isDuplicate =
            msg.Contains(SystemConstants.ErrorKeywords.AlreadyExists) ||
            msg.Contains(SystemConstants.ErrorKeywords.DuplicateCurrency) ||
            msg.Contains(SystemConstants.ErrorKeywords.DuplicateKey);

         if (isDuplicate)
         {
            _logger.Information(
               "{Key}:{Id} already exists on target. " +
               "Attempting update to ensure all fields are set.",
               entity.LogicalName,
               entity.Id
            );

            try
            {
               if (findExistingFunc != null)
               {
                  var targetId = await findExistingFunc(entity, ct);

                  if (targetId.HasValue && targetId.Value != entity.Id)
                  {
                     _logger.Information(
                        "Found alternate key duplicate for " +
                        "{Key}:{Id}. Updating existing record " +
                        "{TargetId} instead.",
                        entity.LogicalName,
                        entity.Id,
                        targetId.Value
                     );

                     entity.Id = targetId.Value;

                     var pkName = await _target.GetPrimaryIdAttributeAsync(
                        entity.LogicalName,
                        ct
                     ) ?? $"{entity.LogicalName}id";

                     if (entity.Attributes.Contains(pkName))
                        entity[pkName] = targetId.Value;
                  }
               }

               if (options.PreserveDates)
                  await _sourceDateService.CreateSourceDateRecordAsync(
                     _target,
                     entity,
                     ct
                  );

               // Try standard update first
               if (updateFunc != null)
                  await updateFunc(entity, ct);
               else
                  await _target.UpdateAsync(entity, ct);

               return (true, string.Empty);
            }
            catch (Exception updateEx)
            {
               var updateMsg = updateEx.Message.ToLower();

               if (updateMsg.Contains(
                  SystemConstants.ErrorKeywords.InvalidStatusCode) &&
                  statusTransitionFunc != null)
               {
                  var success = await statusTransitionFunc(
                     entity,
                     options,
                     ct
                  );

                  if (success)
                     return (true, string.Empty);

                  return (
                     false,
                     FormatFailureMessage(
                        "Status transition failed",
                        updateEx
                     )
                  );
               }

               var isSqlError =
                  updateMsg.Contains(
                     SystemConstants.ErrorKeywords.ForeignKeyConflict
                  ) ||
                  updateMsg.Contains(
                     SystemConstants.ErrorKeywords.ConstraintConflict
                  );

               if (isSqlError && resolveSqlDependencyFunc != null)
               {
                  var success = await resolveSqlDependencyFunc(
                     updateEx.Message,
                     entity,
                     options,
                     ct
                  );

                  if (success)
                     return (true, string.Empty);

                  return (
                     false,
                     FormatFailureMessage(
                        "SQL dependency resolution failed",
                        updateEx
                     )
                  );
               }

               _logger.Warning(
                  "Update failed for existing record {Key}:{Id}: {Msg}",
                  entity.LogicalName,
                  entity.Id,
                  updateEx.Message
               );

               return (
                  false,
                  FormatFailureMessage(
                     "Update failed for existing record",
                     updateEx
                  )
               );
            }
         }

         if (msg.Contains(SystemConstants.ErrorKeywords.InvalidStatusCode) &&
             statusTransitionFunc != null)
         {
            var success = await statusTransitionFunc(
               entity,
               options,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage(
                  "Status transition failed",
                  ex
               )
            );
         }

         if (msg.Contains(SystemConstants.ErrorKeywords.DoesNotExist) &&
             resolveMissingDependencyFunc != null)
         {
            var success = await resolveMissingDependencyFunc(
               ex,
               entity,
               options,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage(
                  "Missing dependency resolution failed",
                  ex
               )
            );
         }

         if (msg.Contains(
            SystemConstants.ErrorKeywords.ForeignKeyConflict) &&
            resolveSqlDependencyFunc != null)
         {
            var success = await resolveSqlDependencyFunc(
               ex.Message,
               entity,
               options,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage(
                  "SQL dependency resolution failed",
                  ex
               )
            );
         }

         var isAttributeError =
            msg.Contains(SystemConstants.ErrorKeywords.CannotBeModified) ||
            msg.Contains(SystemConstants.ErrorKeywords.CannotBeSetOnCreation) ||
            msg.Contains(SystemConstants.ErrorKeywords.OutsideValidRange);

         if (isAttributeError && stripAttributeFunc != null)
         {
            var success = await stripAttributeFunc(
               ex,
               entity,
               options,
               ct
            );

            if (success)
               return (true, string.Empty);

            return (
               false,
               FormatFailureMessage(
                  "Strip attribute retry failed",
                  ex
               )
            );
         }

         _logger.Error(
            ex,
            "Unresolved error for {Key}:{Id}",
            entity.LogicalName,
            entity.Id
         );

         _logger.Information(
            $"FAILED {entity.LogicalName}:{entity.Id} - {ex.Message}"
         );

         return (
            false,
            FormatFailureMessage(
               "Unresolved error",
               ex
            )
         );
      }

      /// <inheritdoc />
      public string FormatFailureMessage(string context, Exception ex)
      {
         return $"{context}: {ex.GetType().Name}: {ex.Message}";
      }
   }
}
