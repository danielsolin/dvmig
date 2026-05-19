using dvmig.Cli.Providers;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class SyncActions(
      ConnectionManager connectionManager,
      IEnvironmentService environmentService,
      ILogger logger,
      IEntityService entityService,
      ISettingsService settingsService
   ) : BaseActions(
      connectionManager,
      environmentService,
      logger,
      entityService,
      settingsService
   )
   {
      public async Task HandleSelectedSyncAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         var (source, target, engine, userResolver, _) =
            await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null ||
            userResolver == null)
            return;

         var selectedEntities = await CliUI.SelectEntitiesAsync(
            EntityService,
            source
         );

         if (selectedEntities == null || selectedEntities.Count == 0)
         {
            CliUI.WriteWarning("No entities selected.".t());

            return;
         }

         if (!await ShowSyncPlanAsync(
            engine,
            userResolver,
            selectedEntities,
            forceResync,
            ct
         ))
            return;

         await ExecuteSyncWorkflowAsync(
            engine,
            source,
            target,
            selectedEntities,
            forceResync,
            ct
         );
      }

      public async Task HandleViewSyncAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         try
         {
            var (source, target, engine, userResolver, syncEntityService) =
               await SetupSyncEngineAsync();

            if (source == null || target == null || engine == null ||
               userResolver == null || syncEntityService == null)
               return;

            var logicalName = await CliUI.SelectEntityAsync(
               EntityService,
               source
            );

            if (string.IsNullOrEmpty(logicalName))
               return;

            var viewInfo = await CliUI.SelectViewAsync(EntityService, source, logicalName);

            if (viewInfo == null)
               return;

            var (viewName, fetchXml) = viewInfo.Value;

            var query = await CliUI.RunStatusAsync(
               "Converting view to query...".t(),
               async () =>
               {
                  var request = new FetchXmlToQueryExpressionRequest
                  {
                     FetchXml = fetchXml
                  };

                  var response = (FetchXmlToQueryExpressionResponse)
                     await source.ExecuteAsync(request, ct);

                  return response.Query;
               }
            );

            if (query == null)
            {
               CliUI.WriteError("Failed to convert View to Query.".t());
               CliUI.Pause();

               return;
            }

            // Enforce all attributes
            query.ColumnSet = await syncEntityService.GetValidColumnsAsync(logicalName, ct);

            var selectedEntities = new List<string> { logicalName };

            if (!await ShowSyncPlanAsync(
               engine,
               userResolver,
               selectedEntities,
               forceResync,
               ct,
               query,
               viewName
            ))
            {
               CliUI.Pause();

               return;
            }

            await ExecuteSyncWorkflowAsync(
               engine,
               source,
               target,
               selectedEntities,
               forceResync,
               ct,
               query
            );
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();

            CliUI.WriteError($"{"Sync failed:".t()} {baseEx.Message}");
            CliUI.Pause();
         }
      }

      public async Task HandleRecommendedSyncAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         var (source, target, engine, userResolver, _) =
            await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null ||
            userResolver == null)
            return;

         var recommendedEntities = SyncSettings.RecommendedEntities.ToList();

         if (!await ShowSyncPlanAsync(
            engine,
            userResolver,
            recommendedEntities,
            forceResync,
            ct
         ))
            return;

         await ExecuteSyncWorkflowAsync(
            engine,
            source,
            target,
            recommendedEntities,
            forceResync,
            ct
         );
      }

      private async Task<bool> ShowSyncPlanAsync(
         ISyncEngine engine,
         IUserService userResolver,
         List<string> entities,
         bool forceResync,
         CancellationToken ct,
         QueryExpression? query = null,
         string? viewName = null
      )
      {
         await CliUI.RunStatusAsync(
            "Preparing synchronization...".t(),
            Logger,
            async () => await engine.InitializeSyncAsync(ct)
         );

         var mappings = await userResolver.GetMappingSummaryAsync(ct);
         var humanMappings = mappings.Where(m => m.IsHuman).ToList();
         var systemCount = mappings.Count - humanMappings.Count;

         var entityTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Seq".t()}[/]")
                  .Centered()
            )
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Entity".t()}[/]")
            );

         for (var i = 0; i < entities.Count; i++)
         {
            var label = entities[i];

            if (query != null && entities.Count == 1 && entities[0] == label)
            {
               var viewLabel = !string.IsNullOrEmpty(viewName) 
                  ? viewName 
                  : "filtered by view".t();

               label += $" {UiMarkup.Grey}({"View: {0}".t(viewLabel)})[/]";
            }

            entityTable.AddRow(
               (i + 1).ToString(),
               $"{UiMarkup.Yellow}{label}[/]"
            );
         }

         var userTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn($"{UiMarkup.BoldCyan}{"Source User".t()}[/]")
            .AddColumn($"{UiMarkup.BoldCyan}{"Target User".t()}[/]")
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Status".t()}[/]")
                  .Centered()
            );

         if (humanMappings.Count == 0)
            userTable.AddRow(
               new Markup($"{UiMarkup.Grey}{"No human users found.".t()}[/]"),
               Text.Empty,
               Text.Empty
            );
         else
            foreach (var mapping in humanMappings)
            {
               var statusColor = mapping.Status == UserMappingStatus.Mapped
                  ? "green"
                  : "yellow";

               userTable.AddRow(
                  mapping.SourceName,
                  mapping.TargetName,
                  $"[{statusColor}]{mapping.Status.ToString().t()}[/]"
               );
            }

         var columns = new Columns(
            new Panel(entityTable)
               .Header($"[bold]{"Entities".t()}[/]")
               .Border(BoxBorder.Rounded)
               .Expand(),
            new Panel(userTable)
               .Header($"[bold]{"User Mappings".t()}[/]")
               .Border(BoxBorder.Rounded)
               .Expand()
         );

         var title = forceResync ? "Re-sync".t() : "Sync".t();
         title = UiMarkup.Red + title + "[/]";

         AnsiConsole.Write(
            new Panel(columns)
               //.Header($"[bold cyan] {title} {"Plan".t()} [/]")
               .Expand()
               .Border(BoxBorder.None)
         );

         if (systemCount > 0)
            AnsiConsole.MarkupLine(
               $"{UiMarkup.Grey}" +
               $"{"Note: {0} system accounts mapped automatically and " +
               "hidden from this view.".t(systemCount)}[/]"
            );

         AnsiConsole.WriteLine();

         if (!AnsiConsole.Confirm(
            $"{"Proceed with this {0} plan?".t(title)}", true
         ))
         {
            CliUI.WriteWarning($"{"{0} cancelled.".t(title)}");

            return false;
         }

         return true;
      }

      private async Task ExecuteSyncWorkflowAsync(
         ISyncEngine engine,
         IDataverseProvider source,
         IDataverseProvider target,
         List<string> entities,
         bool forceResync,
         CancellationToken ct,
         QueryExpression? customQuery = null
      )
      {
         try
         {
            var maxThreads = SettingsService.LoadSettings().MaxParallelism;

            await AnsiConsole.Progress()
               .Columns(
                  new ProgressColumn[]
                  {
                     new EntityColumn(),
                     new RecordCountColumn(),
                     new SpeedColumn(),
                     new ProgressBarColumn(),
                     new PercentageColumn(),
                     new RemainingTimeColumn(),
                     new SpinnerColumn(),
                  }
               )
               .StartAsync(
                  async ctx =>
                  {
                     foreach (var rawLogicalName in entities)
                     {
                        var logicalName = rawLogicalName.ToLowerInvariant();

                        var displayName = char.ToUpper(logicalName[0]) +
                           logicalName.Substring(1);

                        var query = (customQuery != null &&
                                     customQuery.EntityName.Equals(
                                        logicalName,
                                        StringComparison.OrdinalIgnoreCase
                                     ))
                           ? customQuery
                           : null;

                        var sourceCountTask = source.GetRecordCountAsync(
                           logicalName,
                           query,
                           ct
                        );

                        var targetCountTask = forceResync
                           ? Task.FromResult(0L)
                           : target.GetRecordCountAsync(
                              logicalName,
                              null,
                              ct
                           );

                        await Task.WhenAll(sourceCountTask, targetCountTask);

                        var totalCount = await sourceCountTask;
                        var targetCount = await targetCountTask;

                        if (totalCount == 0)
                        {
                           AnsiConsole.MarkupLine(
                              $"{UiMarkup.Grey}" +
                              $"{"No records found for {0}.".t(
                                 logicalName)}[/]"
                           );

                           continue;
                        }

                        var initialProcessed = (int)Math.Min(
                           totalCount,
                           targetCount
                        );

                        var task = ctx.AddTask(
                           "Initializing...".t(),
                           true,
                           totalCount
                        );

                        task.Value = initialProcessed;

                        //var actionTitle = forceResync
                        //   ? "Re-syncing".t()
                        //   : "Migrating".t();
                        var actionTitle = "";

                        var progressProvider = new MigrationProgressProvider(
                           task,
                           maxThreads,
                           actionTitle,
                           displayName,
                           totalCount,
                           initialProcessed
                        );

                        var options = new SyncOptions
                        {
                           StripMissingDependencies = true,
                           AutoCreateRelatedRecords =
                              SettingsService.LoadSettings()
                                 .AutoCreateRelatedRecords,
                           MaxDegreeOfParallelism = maxThreads,
                           ForceResync = forceResync,
                           PreserveAuditData = true
                        };

                        Logger.AttachProgress(
                           new Progress<string>(
                              msg =>
                              {
                                 var isCritical =
                                    msg.Contains(
                                       UiMarkup.Wait,
                                       StringComparison.Ordinal
                                    ) ||
                                    msg.Contains(
                                       ErrorKeywords.TooManyRequests,
                                       StringComparison.OrdinalIgnoreCase
                                    ) ||
                                    msg.StartsWith(UiMarkup.Yellow) ||
                                    msg.StartsWith(UiMarkup.Red);

                                 if (isCritical)
                                    AnsiConsole.MarkupLine(msg);
                              }
                           )
                        );

                        try
                        {
                           await engine.SyncAsync(
                              logicalName,
                              options,
                              query,
                              progressProvider.GetProgressReporter(),
                              ct
                           );
                        }
                        catch (Exception ex)
                        {
                           var baseEx = ex.GetBaseException();

                           CliUI.WriteError(
                              $"{"Sync aborted for {0}:".t(logicalName)} " +
                              $"{baseEx.Message}"
                           );
                        }
                        finally
                        {
                           Logger.DetachProgress();
                           progressProvider.FinalizeProgress();
                        }
                     }
                  }
               );

            var actionName = forceResync ? "Re-sync".t() : "Migration".t();
            CliUI.WriteSuccess($"{"{0} Finished!".t(actionName)}");
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();

            CliUI.WriteError($"{"Sync failed:".t()} {baseEx.Message}");
         }

         CliUI.Pause();
      }
   }
}
