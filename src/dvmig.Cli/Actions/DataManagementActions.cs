using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli.Actions
{
   public class DataManagementActions(
      ConnectionManager connectionManager,
      ISeedingService seedingService,
      IWipeDataService wipeDataService,
      IEnvironmentService environmentService,
      IEntityService entityService,
      ILogger logger,
      ISettingsService settingsService
   ) : BaseActions(
      connectionManager,
      environmentService,
      logger,
      entityService,
      settingsService
   )
   {
      private readonly ISeedingService _seedingService = seedingService;
      private readonly IWipeDataService _wipeDataService = wipeDataService;

      public async Task HandleSeedingAsync(CancellationToken ct)
      {
         var provider = await ConnectionManager.ConnectAsync(
            ConnectionDirection.Source
         );

         if (provider == null)
            return;

         var prompt =
            $"How many {UiMarkup.BoldBlue}Accounts[/] " +
            "(with related Contacts and Activities) would you like " +
            "to generate?";

         var count = AnsiConsole.Ask<int>(prompt, 100);

         await CliUI.RunStatusAsync(
            "Seeding data...",
            Logger,
            async () => await _seedingService.SeedSampleDataAsync(
               provider,
               count,
               ct
            )
         );

         CliUI.WriteSuccess("Seeding Finished!");
      }

      private enum WipeTargetChoice
      {
         AllRecommended,
         SpecificEntities
      }

      public async Task HandleSourceDataCleanupAsync(CancellationToken ct)
      {
         await HandleDataCleanupAsync(ConnectionDirection.Source, ct);
      }

      public async Task HandleTargetDataCleanupAsync(CancellationToken ct)
      {
         await HandleDataCleanupAsync(ConnectionDirection.Target, ct);
      }

      private async Task HandleDataCleanupAsync(
         ConnectionDirection direction,
         CancellationToken ct
      )
      {
         var provider = await ConnectionManager.ConnectAsync(direction);

         if (provider == null)
            return;

         var envName = direction == ConnectionDirection.Source
            ? "SOURCE"
            : "TARGET";

         var wipeChoice = AnsiConsole.Prompt(
            new SelectionPrompt<WipeTargetChoice>()
               .Title($"What data do you want to wipe on {envName}?")
               .UseConverter(c => c switch
               {
                  WipeTargetChoice.AllRecommended => "All Recommended Entities",
                  WipeTargetChoice.SpecificEntities => 
                     "Select Specific Entities",
                  _ => throw new ArgumentOutOfRangeException()
               })
               .AddChoices(Enum.GetValues<WipeTargetChoice>())
         );

         List<string>? selectedEntities = null;

         if (wipeChoice == WipeTargetChoice.SpecificEntities)
         {
            selectedEntities = await CliUI.SelectEntitiesAsync(
               EntityService,
               provider
            );

            if (selectedEntities == null || selectedEntities.Count == 0)
            {
               CliUI.WriteWarning("No entities selected. Wipe cancelled.");

               return;
            }
         }
         else
         {
            var list = SyncSettings.RecommendedEntities
               .Select(e => char.ToUpper(e[0]) + e.Substring(1))
               .ToList();

            var entityNames = list.Count > 1
               ? string.Join(", ", list.Take(list.Count - 1)) +
                 " and " + list.Last()
               : list.FirstOrDefault() ?? string.Empty;

            var warningTable = new Table()
               .Border(TableBorder.None)
               .HideHeaders()
               .AddColumn("Icon")
               .AddColumn("Message");

            warningTable.AddRow(
               "[bold red]![/]",
               $"{UiMarkup.BoldRed}CRITICAL WARNING:[/] This " +
               "operation will delete [bold]EVERY SINGLE[/] " +
               $"{entityNames} record from the {envName} environment."
            );

            warningTable.AddRow(
               "[bold red]![/]",
               $"{UiMarkup.Red}This is NOT restricted to " +
               "test data. Real data will be destroyed.[/]"
            );

            warningTable.AddRow(
               "[bold red]![/]",
               $"{UiMarkup.Red}This action is permanent and " +
               "irreversible.[/]"
            );

            AnsiConsole.Write(
               new Panel(warningTable)
                  .Header("[bold red] DANGER [/]")
                  .BorderColor(Color.Red)
            );
         }

         var wipeText = UiMarkup.WipeDataConfirmation;
         var prompt =
            $"Type {UiMarkup.BoldRed}{wipeText}[/] " +
            "to confirm:";

         var confirmation = AnsiConsole.Ask<string>(prompt);

         if (confirmation != UiMarkup.WipeDataConfirmation)
         {
            CliUI.WriteWarning("Wipe cancelled.");

            return;
         }

         try
         {
            long remainingRecords = -1;
            long initialRecords = -1;
            var currentStatus = "Initializing wipe...".t();
            var startTime = DateTime.Now;

            var progress = new Progress<long>(
               count =>
               {
                  remainingRecords = count;
                  if (initialRecords == -1)
                     initialRecords = count;
               }
            );

            var statusProgress = new Progress<string>(
               status => currentStatus = status
            );

            await AnsiConsole.Status()
               .StartAsync(
                  currentStatus,
                  async ctx =>
                  {
                     var cleanupTask = _wipeDataService.WipeEntitiesAsync(
                        provider,
                        selectedEntities,
                        progress,
                        statusProgress,
                        ct
                     );

                     while (!cleanupTask.IsCompleted)
                     {
                        var statusLine = $"[yellow]{currentStatus}[/]";

                        if (remainingRecords >= 0)
                        {
                           var elapsed = DateTime.Now - startTime;
                           var deleted = initialRecords - remainingRecords;
                           var etaStr = string.Empty;

                           if (deleted > 0 && elapsed.TotalSeconds > 5)
                           {
                              var recordsPerSec =
                                 deleted / elapsed.TotalSeconds;

                              if (recordsPerSec > 0)
                              {
                                 var remainingSeconds =
                                    remainingRecords / recordsPerSec;
                                 var remainingTime =
                                    TimeSpan.FromSeconds(remainingSeconds);

                                 etaStr =
                                    $" [grey]{remainingTime:hh\\:mm\\:ss}[/]";
                              }
                           }

                           statusLine += 
                              $" {remainingRecords} records remaining..." +
                              etaStr;
                        }

                        ctx.Status(statusLine);

                        await Task.Delay(500, ct);
                     }

                     await cleanupTask;
                  }
               );

            CliUI.WriteSuccess($"Data Wipe Finished for {envName}!");
         }
         catch (OperationCanceledException)
         {
            throw;
         }
         catch (Exception ex)
         {
            var baseEx = ex.GetBaseException();

            CliUI.WriteError($"Data wipe failed: {baseEx.Message}");
         }

         CliUI.Pause();
      }
   }
}
