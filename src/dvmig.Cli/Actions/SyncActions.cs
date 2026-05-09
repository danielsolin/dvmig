using dvmig.Cli.Providers;
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
      IPluginService pluginService,
      IValidationService validator,
      ISchemaService schemaService,
      ILogger logger,
      IEntityService entityService,
      ISettingsService settingsService
      ) : BaseActions(
         connectionManager,
         pluginService,
         validator,
         schemaService,
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
         var (source, target, engine, userResolver) =
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

      public async Task HandleRecommendedSyncAsync(
         CancellationToken ct,
         bool forceResync = false
      )
      {
         var (source, target, engine, userResolver) =
            await SetupSyncEngineAsync();

         if (source == null || target == null || engine == null ||
             userResolver == null)
            return;

         var recommendedEntities = SyncSettings.RecommendedEntities.ToList();

         if (!await ShowSyncPlanAsync(
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
         IUserService userResolver,
         List<string> entities,
         bool forceResync,
         CancellationToken ct
      )
      {
         if (!ConnectionManager.UserMappingsCached)
         {
            await CliUI.RunStatusAsync(
               "Mapping source users to target environment...".t(),
               Logger,
               async () => await userResolver.MapAllSourceUsersAsync(ct)
            );
            ConnectionManager.UserMappingsCached = true;
         }

         var mappings = await userResolver.GetMappingSummaryAsync(ct);
         var humanMappings = mappings.Where(m => m.IsHuman).ToList();
         var systemCount = mappings.Count - humanMappings.Count;

         var title = forceResync ? "Re-sync".t() : "Sync".t();

         var entityTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Seq".t()}[/]").Centered()
            )
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Entity".t()}[/]")
            );

         for (int i = 0; i < entities.Count; i++)
         {
            entityTable.AddRow(
               (i + 1).ToString(),
               $"{UiMarkup.Yellow}{entities[i]}[/]"
            );
         }

         var userTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn($"{UiMarkup.BoldCyan}{"Source User".t()}[/]")
            .AddColumn($"{UiMarkup.BoldCyan}{"Target User".t()}[/]")
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}{"Status".t()}[/]").Centered()
            );

         if (!humanMappings.Any())
         {
            userTable.AddRow(
               new Markup($"{UiMarkup.Grey}{"No human users found.".t()}[/]"),
               Text.Empty,
               Text.Empty
            );
         }
         else
         {
            foreach (var mapping in humanMappings)
            {
               var statusColor = mapping.Status == "Mapped" ? "green" : "yellow";

               userTable.AddRow(
                  mapping.SourceName,
                  mapping.TargetName,
                  $"[{statusColor}]{mapping.Status.t()}[/]"
               );
            }
         }

         var columns = new Columns(
            new Panel(entityTable).Header($"[bold]{"Entities".t()}[/]").Expand(),
            new Panel(userTable).Header($"[bold]{"User Mappings".t()}[/]").Expand()
         );

         AnsiConsole.Write(
            new Panel(columns)
               .Header($"[bold cyan] {title} {"Plan".t()} [/]")
               .Expand()
         );

         if (systemCount > 0)
         {
            AnsiConsole.MarkupLine(
               $"{UiMarkup.Grey}{"Note: {0} system accounts mapped automatically and hidden from this view.".t(systemCount)}[/]"
            );
         }

         AnsiConsole.WriteLine();

         if (!AnsiConsole.Confirm($"{"Proceed with this {0} plan?".t(title)}", true))
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
         CancellationToken ct
      )
      {
         var maxThreads = SettingsService.LoadSettings().MaxParallelism;

         await AnsiConsole.Progress()
            .Columns(
               new ProgressColumn[]
               {
                  new TaskDescriptionColumn(),
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

                     var actionTitle = forceResync 
                        ? "Re-syncing".t() 
                        : "Migrating".t();
                     var displayName = char.ToUpper(logicalName[0]) +
                        logicalName.Substring(1);

                     var sourceCountTask = source.GetRecordCountAsync(
                        logicalName,
                        ct
                     );

                     var targetCountTask = forceResync
                        ? Task.FromResult(0L)
                        : target.GetRecordCountAsync(
                           logicalName,
                           ct
                        );

                     await Task.WhenAll(sourceCountTask, targetCountTask);

                     long totalCount = await sourceCountTask;
                     long targetCount = await targetCountTask;

                     if (totalCount == 0)
                     {
                        AnsiConsole.MarkupLine(
                           $"{UiMarkup.Grey}{"No records found for {0}.".t(logicalName)}[/]"
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
                        MaxDegreeOfParallelism = maxThreads,
                        ForceResync = forceResync,
                        PreserveAuditData = true
                     };

                     Logger.AttachProgress(
                        new Progress<string>(
                           msg =>
                           {
                              bool isCritical =
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
                           null,
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

         CliUI.Pause();
      }
   }
}
