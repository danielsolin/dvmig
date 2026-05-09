using System.Reflection;
using System.Runtime.InteropServices;

using dvmig.Cli.Actions;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;

using Spectre.Console;

namespace dvmig.Cli
{
   /// <summary>
   /// Encapsulates repetitive Spectre.Console UI patterns for the CLI.
   /// </summary>
   public static class CliUI
   {
      /// <summary>
      /// Represents an item in the main menu.
      /// </summary>
      /// <param name="Label">The display label.</param>
      /// <param name="Action">The action to execute.</param>
      public record MenuItem(
         string Label,
         Func<CancellationToken, Task>? Action
      );

      /// <summary>
      /// Runs an asynchronous task with a spinning status indicator and 
      /// provides a progress reporter that logs to the console.
      /// </summary>
      /// <param name="statusMessage">The message to display.</param>
      /// <param name="action">The asynchronous action to execute.</param>
      /// <param name="logger">The logger instance.</param>
      public static async Task RunStatusAsync(
         string statusMessage,
         ILogger logger,
         Func<Task> action
      )
      {
         await AnsiConsole.Status()
            .StartAsync(statusMessage, async ctx =>
            {
               var progress = new Progress<string>(msg =>
               {
                  bool isPersistent =
                     msg.StartsWith(SystemConstants.UiMarkup.Yellow) ||
                     msg.StartsWith(SystemConstants.UiMarkup.Red);

                  if (isPersistent)
                  {
                     AnsiConsole.MarkupLine(
                        $"{SystemConstants.UiMarkup.Grey}" +
                        $"[[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                     );
                  }
                  else
                  {
                     ctx.Status(msg);
                  }
               });

               logger.AttachProgress(progress);

               try
               {
                  await action();
               }
               finally
               {
                  logger.DetachProgress();
               }
            });
      }

      /// <summary>
      /// Runs an asynchronous task with a spinning status indicator that 
      /// returns a value.
      /// </summary>
      /// <typeparam name="T">The return type.</typeparam>
      /// <param name="statusMessage">The message to display.</param>
      /// <param name="action">The asynchronous action to execute.</param>
      public static async Task<T> RunStatusAsync<T>(
         string statusMessage,
         Func<Task<T>> action
      )
      {

         return await AnsiConsole.Status()
            .StartAsync(statusMessage, async ctx => await action());
      }

      public static void WriteHeader()
      {
         AnsiConsole.Clear();
         AnsiConsole.Write(new FigletText("DVMIG").Color(Color.Blue));

         var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "Unknown";

         if (version.Contains('+'))
            version = version[..version.IndexOf('+')];

         AnsiConsole.MarkupLine(
            $"[bold]Dataverse Migrator (v{version})[/]"
         );

         AnsiConsole.WriteLine();
      }

      public static void WriteSuccess(string message)
      {
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.BoldGreen}{message}[/]"
         );
      }

      public static void WriteWarning(string message)
      {
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Yellow}{message}[/]"
         );
      }

      public static void WriteError(string message)
      {
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Red}{message}[/]"
         );
      }

      public static void Pause()
      {
         AnsiConsole.WriteLine();
         AnsiConsole.MarkupLine(
            $"{SystemConstants.UiMarkup.Grey}" +
            "Press any key to return to menu...".t() + "[/]"
         );

         Console.ReadKey(true);
      }

      public static bool Confirm(string message, bool defaultValue = true)
      {
         return AnsiConsole.Confirm(message, defaultValue);
      }

      public static MenuItem PromptMainMenu(
         SyncActions syncActions,
         MaintenanceActions maintenanceActions,
         SettingsActions settingsActions,
         Action onExit
      )
      {
         var mainPrompt = new SelectionPrompt<MenuItem>()
            .Title("What would you like to do?".t())
            .PageSize(15)
            .UseConverter(m => m.Label)
            .HighlightStyle(new Style
            {
               Foreground = Color.MediumOrchid
            });

         var syncGroup = new List<MenuItem>
         {
            new(
               "Sync Recommended".t(),
               ct => syncActions.HandleRecommendedSyncAsync(ct, false)
            ),
            new(
               $"{"Sync Selected".t()} {SystemConstants.UiMarkup.Grey}" +
               $"({"pick entities".t()})[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, false)
            ),
            new(
               "Re-sync Recommended".t(),
               ct => syncActions.HandleRecommendedSyncAsync(ct, true)
            ),
            new(
               $"{"Re-sync Selected".t()} {SystemConstants.UiMarkup.Grey}" +
               $"({"pick entities".t()})[/]",
               ct => syncActions.HandleSelectedSyncAsync(ct, true)
            )
         };
         
         var maintenanceGroup = new List<MenuItem>
            {
               new(
                  $"{"Install DVMig Components".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Target".t()})[/]",
                  maintenanceActions.HandleInstallMenuAsync
               ),
               new(
                  $"{"Uninstall DVMig Components".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Target".t()})[/]",
                  maintenanceActions.HandleTargetComponentsCleanupAsync
               ),
               new(
                  "View Recorded Migration Failures".t(),
                  maintenanceActions.HandleViewFailuresAsync
               ),
            };

         var dataGroup = new List<MenuItem> {
               new(
                  $"{"Generate Sample Data".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Source".t()})[/]",
                  maintenanceActions.HandleSeedingAsync
               ),
               new(
                  $"{"Wipe Data on Source".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Caution!".t()})[/]",
                  maintenanceActions.HandleSourceDataCleanupAsync
               ),
               new(
                  $"{"Wipe Data on Target".t()} " +
                  $"{SystemConstants.UiMarkup.Grey}({"Caution!".t()})[/]",
                  maintenanceActions.HandleTargetDataCleanupAsync
               )
            };

         mainPrompt.AddChoiceGroup(
            new MenuItem(
               $"🚀 {SystemConstants.UiMarkup.BoldGreen}" + 
               "Synchronization".t() + "[/]",
               null
            ),
            syncGroup
         );

         mainPrompt.AddChoiceGroup(
            new MenuItem(
               $"🛠️ {SystemConstants.UiMarkup.BoldCyan}" + 
               "Maintenance".t() + "[/]",
               null
            ),
            maintenanceGroup
         );

         mainPrompt.AddChoiceGroup(
            new MenuItem(
               $"🧪 {SystemConstants.UiMarkup.BoldMagenta}" +
               "Data Management".t() + "[/]",
               null
            ),
            dataGroup
         );

         mainPrompt.AddChoices(
            new[]
            {
               new MenuItem(
                  "Settings".t(),
                  settingsActions.HandleSettingsMenuAsync
               ),
               new MenuItem(
                  "Exit".t(),
                  (ct) =>
                  {
                     onExit();

                     return Task.CompletedTask;
                  }
               )
            }
         );

         return AnsiConsole.Prompt(mainPrompt);
      }

      public static async Task<List<string>?> SelectEntitiesAsync(
         IEntityService entityService,
         IDataverseProvider provider
      )
      {
         var entities = await RunStatusAsync(
            "Fetching entity metadata...".t(),
            async () =>
            {
               try
               {
                  return await entityService
                     .GetMigrationEntitiesAsync(
                        provider,
                        default
                     );
               }
               catch (Exception ex)
               {
                  WriteError(
                     $"Failed to fetch metadata: {ex.Message}"
                  );

                  return null;
               }
            }
         );

         if (entities == null || entities.Count == 0)
            return null;

         var prompt = new MultiSelectionPrompt<string>()
            .Title(
               $"Select {SystemConstants.UiMarkup.Green}Entities[/] " +
               "to migrate:"
            )
            .PageSize(15)
            .MoreChoicesText(
               $"{SystemConstants.UiMarkup.Grey}" +
               "(Move up and down to reveal more)[/]"
            )
            .InstructionsText(
               $"{SystemConstants.UiMarkup.Grey}(Press " +
               $"{SystemConstants.UiMarkup.Blue}<space>[/] to toggle, " +
               $"{SystemConstants.UiMarkup.Green}<enter>[/] to accept)[/]"
            );

         foreach (var entity in entities)
            prompt.AddChoice(entity.DisplayName.UserLocalizedLabel.Label);

         return AnsiConsole.Prompt(prompt);
      }
   }
}
