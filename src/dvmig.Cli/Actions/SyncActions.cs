using dvmig.Cli.Providers;
using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Synchronization;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class SyncActions : BaseActions
   {
      public SyncActions(
         ConnectionManager connectionManager,
         IPluginService pluginService,
         IValidationService validator,
         ISchemaService schemaService,
         ILogger logger,
         IEntityService entityService
      )
         : base(
            connectionManager,
            pluginService,
            validator,
            schemaService,
            logger,
            entityService
         )
      {
      }

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
            CliUI.WriteWarning("No entities selected.");

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
         await CliUI.RunStatusAsync(
            "Mapping source users to target environment...",
            Logger,
            async () => await userResolver.MapAllSourceUsersAsync(ct)
         );

         var mappings = await userResolver.GetMappingSummaryAsync(ct);
         var humanMappings = mappings.Where(m => m.IsHuman).ToList();
         var systemCount = mappings.Count - humanMappings.Count;

         var title = forceResync ? "Re-sync" : "Sync";

         var entityTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn(new TableColumn($"{UiMarkup.BoldCyan}Seq[/]").Centered())
            .AddColumn(new TableColumn($"{UiMarkup.BoldCyan}Entity[/]"));

         for (int i = 0; i < entities.Count; i++)
         {
            entityTable.AddRow(
               (i + 1).ToString(),
               $"{UiMarkup.Yellow}{entities[i]}[/]"
            );
         }

         var userTable = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn($"{UiMarkup.BoldCyan}Source User[/]")
            .AddColumn($"{UiMarkup.BoldCyan}Target User[/]")
            .AddColumn(
               new TableColumn($"{UiMarkup.BoldCyan}Status[/]").Centered()
            );

         if (!humanMappings.Any())
         {
            userTable.AddRow(
               new Markup($"{UiMarkup.Grey}No human users found.[/]"),
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
                  $"[{statusColor}]{mapping.Status}[/]"
               );
            }
         }

         var columns = new Columns(
            new Panel(entityTable).Header("Entities").Expand(),
            new Panel(userTable).Header("User Mappings").Expand()
         );

         AnsiConsole.Write(
            new Panel(columns)
               .Header($"[bold cyan] {title} Plan [/]")
               .Expand()
         );

         if (systemCount > 0)
         {
            AnsiConsole.MarkupLine(
               $"{UiMarkup.Grey}Note: {systemCount} system accounts mapped " +
               "automatically and hidden from this view.[/]"
            );
         }

         AnsiConsole.WriteLine();

         if (!AnsiConsole.Confirm($"Proceed with this {title} plan?", true))
         {
            CliUI.WriteWarning($"{title} cancelled.");

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
         var maxThreads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title(
                  $"Select {UiMarkup.Green}Max Parallelism[/]"
                  + " (Threads):"
               )
               .AddChoices(SyncSettings.ParallelismOptions)
         );

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

                     var actionTitle = forceResync ? "Re-syncing" : "Migrating";
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
                           $"{UiMarkup.Grey}No records found for " +
                           $"{logicalName}.[/]"
                        );

                        continue;
                     }

                     var initialProcessed = (int)Math.Min(
                        totalCount,
                        targetCount
                     );

                     var task = ctx.AddTask(
                        "Initializing...",
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
                           $"Sync aborted for {logicalName}: " +
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

         var actionName = forceResync ? "Re-sync" : "Migration";
         CliUI.WriteSuccess($"{actionName} Finished!");
      }
   }
}
