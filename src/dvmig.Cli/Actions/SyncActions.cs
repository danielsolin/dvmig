using dvmig.Cli.Providers;
using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class SyncActions : BaseActions
   {
      private readonly IMetadataService _metadataService;

      public SyncActions(
         ConnectionManager connectionManager,
         IMetadataService metadataService,
         IPluginService pluginService,
         ISourceDataService sourceDataService,
         IValidationService validator,
         ISchemaService schemaService,
         ILogger logger
      )
         : base(
            connectionManager,
            pluginService,
            sourceDataService,
            validator,
            schemaService,
            logger
         )
      {
         _metadataService = metadataService;
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
            _metadataService,
            source
         );

         if (selectedEntities == null || selectedEntities.Count == 0)
         {
            CliUI.WriteWarning("No entities selected.");

            return;
         }

         if (!await PrepareUserMappingsAsync(userResolver, ct))
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

         var title = forceResync ? "Re-sync" : "Sync";
         AnsiConsole.MarkupLine(
            $"{UiMarkup.BoldCyan}Recommended {title} Order:[/]"
         );

         foreach (var entity in recommendedEntities)
            AnsiConsole.MarkupLine($" - {entity}");

         if (!AnsiConsole.Confirm($"Proceed with this {title} plan?", true))
         {
            CliUI.WriteWarning($"Recommended {title} cancelled.");

            return;
         }

         if (!await PrepareUserMappingsAsync(userResolver, ct))
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

      private async Task<bool> PrepareUserMappingsAsync(
         IUserResolver userResolver,
         CancellationToken ct
      )
      {
         AnsiConsole.MarkupLine(
            $"\n{UiMarkup.BoldCyan}Phase: User Mapping Verification[/]"
         );

         await CliUI.RunStatusAsync(
            "Mapping source users to target environment...",
            Logger,
            async () => await userResolver.MapAllSourceUsersAsync(ct)
         );

         var mappings = await userResolver.GetMappingSummaryAsync(ct);

         if (!mappings.Any())
         {
            CliUI.WriteWarning("No active users found to map.");

            return true;
         }

         var humanMappings = mappings.Where(m => m.IsHuman).ToList();
         var systemCount = mappings.Count - humanMappings.Count;

         var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Source User")
            .AddColumn("Target User")
            .AddColumn("Status");

         foreach (var mapping in humanMappings)
         {
            var statusColor = mapping.Status == "Mapped" ? "green" : "yellow";

            table.AddRow(
               mapping.SourceName,
               mapping.TargetName,
               $"[{statusColor}]{mapping.Status}[/]"
            );
         }

         AnsiConsole.Write(table);

         if (systemCount > 0)
         {
            AnsiConsole.MarkupLine(
               $"{UiMarkup.Grey}Note: {systemCount} system accounts mapped " +
               "automatically and hidden from this view.[/]"
            );
         }

         AnsiConsole.WriteLine();

         if (!AnsiConsole.Confirm(
            "Accept these user mappings and proceed with migration?",
            true
         ))
         {
            CliUI.WriteWarning("Migration cancelled by user.");

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

                     var sourceCountTask = _metadataService.GetRecordCountAsync(
                        source,
                        logicalName,
                        ct
                     );

                     var targetCountTask = forceResync
                        ? Task.FromResult(0L)
                        : _metadataService.GetRecordCountAsync(
                           target,
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

                     var initialProcessed = (int)Math.Min(totalCount, targetCount);

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
