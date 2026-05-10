using System.Reflection;

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
      /// <param name="lineByLine">Whether to print steps line-by-line.</param>
      public static async Task RunStatusAsync(
         string statusMessage,
         ILogger logger,
         Func<Task> action,
         bool lineByLine = false
      )
      {
         await AnsiConsole.Status()
            .StartAsync(statusMessage, async ctx =>
            {
               var progress = new Progress<string>(msg =>
               {
                  bool isPersistent = lineByLine ||
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
         var sections = new List<(string Header, List<MenuItem> Items)>
         {
            (
               $"🚀 {SystemConstants.UiMarkup.BoldGreen}" +
               "Synchronization".t() + "[/]",
               new List<MenuItem>
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
               }
            ),
            (
               $"🛠️ {SystemConstants.UiMarkup.BoldCyan}" +
               "Maintenance".t() + "[/]",
               new List<MenuItem>
               {
                  new(
                     $"{"Install DVMig Components".t()} " +
                     $"{SystemConstants.UiMarkup.Grey}({"Target".t()})[/]",
                     maintenanceActions.HandleInstallComponentsAsync
                  ),
                  new(
                     $"{"Uninstall DVMig Components".t()} " +
                     $"{SystemConstants.UiMarkup.Grey}({"Target".t()})[/]",
                     maintenanceActions.HandleUninstallComponentsAsync
                  ),
                  new(
                     "View Recorded Migration Failures".t(),
                     maintenanceActions.HandleViewFailuresAsync
                  ),
               }
            ),
            (
               $"🧪 {SystemConstants.UiMarkup.BoldMagenta}" +
               "Data Management".t() + "[/]",
               new List<MenuItem>
               {
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
               }
            ),
            (
               string.Empty,
               new List<MenuItem>
               {
                  new(
                     "Settings".t(),
                     settingsActions.HandleSettingsMenuAsync
                  ),
                  new(
                     "Exit".t(),
                     ct =>
                     {
                        onExit();

                        return Task.CompletedTask;
                     }
                  )
               }
            )
         };

         var flatItems = sections.SelectMany(s => s.Items).ToList();
         int selectedIndex = 0;

         return AnsiConsole.Live(new Table())
            .Start(ctx =>
            {
               while (true)
               {
                  var table = new Table()
                     .Border(TableBorder.Rounded)
                     .AddColumn(
                        new TableColumn(
                           $"[bold white]{"What would you like to do?".t()}[/]"
                        ).LeftAligned()
                     );

                  table.Expand = false;

                  foreach (var section in sections)
                  {
                     // Fixed-length string instead of Rule to prevent full-
                     // width expansion.
                     if (sections.IndexOf(section) > 0)
                        table.AddRow(
                           "[grey]─────────────────────────────────────────[/]"
                        );

                     var content = new Grid().AddColumn();

                     if (!string.IsNullOrEmpty(section.Header))
                        content.AddRow($"[bold]{section.Header}[/]");

                     foreach (var item in section.Items)
                     {
                        int itemIndex = flatItems.IndexOf(item);
                        bool isSelected = itemIndex == selectedIndex;

                        var prefix = isSelected ? "> " : "  ";
                        var style = isSelected ? "bold springgreen1" : "white";

                        content.AddRow($"[{style}]{prefix}{item.Label}[/]");
                     }

                     table.AddRow(content);
                  }

                  ctx.UpdateTarget(table);

                  var key = Console.ReadKey(true);

                  if (key.Key == ConsoleKey.UpArrow)
                     selectedIndex = (selectedIndex - 1 + flatItems.Count) %
                        flatItems.Count;
                  else if (key.Key == ConsoleKey.DownArrow)
                     selectedIndex = (selectedIndex + 1) % flatItems.Count;
                  else if (key.Key == ConsoleKey.Enter)
                     return flatItems[selectedIndex];
               }
            });
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
