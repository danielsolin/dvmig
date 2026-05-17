using System.Reflection;
using Microsoft.Xrm.Sdk.Metadata;
using dvmig.Cli.Actions;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Settings;
using Spectre.Console;
using Spectre.Console.Rendering;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli
{
   using EntityMetadata = Microsoft.Xrm.Sdk.Metadata.EntityMetadata;

   /// <summary>
   /// Encapsulates repetitive Spectre.Console UI patterns for the CLI.
   /// </summary>
   public static class CliUI
   {
      /// <summary>
      /// Gets or sets the connection manager used to display environment info.
      /// </summary>
      public static ConnectionManager? ConnectionManager { get; set; }

      /// <summary>
      /// Gets or sets the settings service used to retrieve stored URIs.
      /// </summary>
      public static ISettingsService? SettingsService { get; set; }

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
                  var isPersistent = lineByLine ||
                     msg.StartsWith(UiMarkup.Yellow) ||
                     msg.StartsWith(UiMarkup.Red);

                  if (isPersistent)
                     AnsiConsole.MarkupLine(
                        $"{UiMarkup.Grey}" +
                        $"[[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                     );
                  else
                     ctx.Status(msg);
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
            .StartAsync(statusMessage, async ctx =>
            {
               try
               {
                  return await action();
               }
               catch (Exception ex)
               {
                  AnsiConsole.WriteLine();
                  WriteError(
                     $"Status task failed: {ex.GetBaseException().Message}"
                  );

                  return default!;
               }
            });
      }

      public static void WriteHeader(bool includeEnvironments = true)
      {
         AnsiConsole.Clear();
         AnsiConsole.Write(new FigletText("DVMIG").Color(Color.Blue));

         var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "Unknown";

         if (version.Contains('+'))
            version = version[..version.IndexOf('+')];

         AnsiConsole.MarkupLine(
            $"[bold]Dataverse Migrator (v{version})[/]\n" +
            $"{UiMarkup.Grey}by daniel@solin.org[/]"
         );

         AnsiConsole.WriteLine();

         if (includeEnvironments)
            WriteEnvironments();
      }

      public static void WriteEnvironments()
      {
         var panel = GetEnvironmentsPanel();

         if (panel != null)
         {
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
         }
      }

      public static Panel? GetEnvironmentsPanel()
      {
         if(ConnectionManager == null || SettingsService == null)
            return null;

         var settings = SettingsService.LoadSettings();

         var source = ConnectionManager.GetActiveConnection(
            ConnectionDirection.Source
         );

         var target = ConnectionManager.GetActiveConnection(
            ConnectionDirection.Target
         );

         var sourceUrl = StringMasker.GetEnvironmentUrl(
            settings.SourceConnectionString
         );

         var targetUrl = StringMasker.GetEnvironmentUrl(
            settings.TargetConnectionString
         );

         var sourceLabel = source != null
            ? $"{UiMarkup.BoldGreen}{GetProviderUrl(source)}[/]"
            : $"{UiMarkup.Grey}{sourceUrl}[/]";

         var targetLabel = target != null
            ? $"{UiMarkup.BoldGreen}{GetProviderUrl(target)}[/]"
            : $"{UiMarkup.Grey}{targetUrl}[/]";

         return new Panel(
               new Grid()
                  .AddColumn(new GridColumn().NoWrap())
                  .AddColumn(new GridColumn().PadLeft(2))
                  .AddRow(
                     $"{UiMarkup.BoldCyan}Source:[/]",
                     sourceLabel
                  )
                  .AddRow(
                     $"{UiMarkup.BoldCyan}Target:[/]",
                     targetLabel
                  )
            )
            .Header($"Environments".t())
            .Border(BoxBorder.Rounded)
            .Expand();
      }

      private static string GetProviderUrl(IDataverseProvider provider)
      {
         var conn = provider.ConnectionString;

         return conn.Contains("://")
            ? conn.Split("://")[1].Split(";")[0].Split("/")[0]
            : "Connected";
      }

      public static void WriteSuccess(string message)
      {
         AnsiConsole.MarkupLine(
            $"{UiMarkup.BoldGreen}{message}[/]"
         );
      }

      public static void WriteWarning(string message)
      {
         AnsiConsole.MarkupLine(
            $"{UiMarkup.Yellow}{message}[/]"
         );
      }

      public static void WriteError(string message)
      {
         AnsiConsole.MarkupLine(
            $"{UiMarkup.Red}{message}[/]"
         );
      }

      public static void Pause()
      {
         AnsiConsole.WriteLine();
         AnsiConsole.MarkupLine(
            $"{UiMarkup.Grey}" +
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
         DataManagementActions dataManagementActions,
         SettingsActions settingsActions,
         Action onExit
      )
      {
         WriteHeader(false);

         var sections = new List<(string Header, List<MenuItem> Items)>
         {
            (
               $"🚀 {UiMarkup.BoldGreen}" +
               "Synchronization".t() + "[/]",
               new List<MenuItem>
               {
                  new(
                     "Sync Recommended".t(),
                     ct => syncActions.HandleRecommendedSyncAsync(ct, false)
                  ),
                  new(
                     $"{"Sync Selected".t()} " +
                     $"{UiMarkup.Grey}" +
                     $"({"pick entities".t()})[/]",
                     ct => syncActions.HandleSelectedSyncAsync(ct, false)
                  ),
                  new(
                     "Re-sync Recommended".t(),
                     ct => syncActions.HandleRecommendedSyncAsync(ct, true)
                  ),
                  new(
                     $"{"Re-sync Selected".t()} " +
                     $"{UiMarkup.Grey}" +
                     $"({"pick entities".t()})[/]",
                     ct => syncActions.HandleSelectedSyncAsync(ct, true)
                  )
               }
            ),
            (
               $"🛠️ {UiMarkup.BoldCyan}" +
               "Maintenance".t() + "[/]",
               new List<MenuItem>
               {
                  new(
                     $"{"Install DVMig Components".t()} " +
                     $"{UiMarkup.Grey}({"Target".t()})[/]",
                     maintenanceActions.HandleInstallComponentsAsync
                  ),
                  new(
                     $"{"Uninstall DVMig Components".t()} " +
                     $"{UiMarkup.Grey}({"Target".t()})[/]",
                     maintenanceActions.HandleUninstallComponentsAsync
                  ),
                  new(
                     "View Recorded Migration Failures".t(),
                     maintenanceActions.HandleViewFailuresAsync
                  ),
               }
            ),
            (
               $"🧪 {UiMarkup.BoldMagenta}" +
               "Data Management".t() + "[/]",
               new List<MenuItem>
               {
                  new(
                     $"{"Generate Sample Data".t()} " +
                     $"{UiMarkup.Grey}({"Source".t()})[/]",
                     dataManagementActions.HandleSeedingAsync
                  ),
                  new(
                     $"{"Wipe Data on Source".t()} " +
                     $"{UiMarkup.Grey}({"Caution!".t()})[/]",
                     dataManagementActions.HandleSourceDataCleanupAsync
                  ),
                  new(
                     $"{"Wipe Data on Target".t()} " +
                     $"{UiMarkup.Grey}({"Caution!".t()})[/]",
                     dataManagementActions.HandleTargetDataCleanupAsync
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
         var selectedIndex = 0;

         return AnsiConsole.Live(new Table())
            .Start(ctx =>
            {
               while (true)
               {
                  var header = (IRenderable?)GetEnvironmentsPanel() ?? new Markup("");

                  var table = new Table()
                     .Border(TableBorder.Rounded)
                     .AddColumn(new TableColumn(header).LeftAligned());

                  table.Expand = false;

                  foreach (var section in sections)
                  {
                     if (sections.IndexOf(section) > 0)
                        table.AddRow(
                           "[grey]─────────────────────────────────────────[/]"
                        );

                     var content = new Grid().AddColumn();

                     if (!string.IsNullOrEmpty(section.Header))
                        content.AddRow($"[bold]{section.Header}[/]");

                     foreach (var item in section.Items)
                     {
                        var itemIndex = flatItems.IndexOf(item);
                        var isSelected = itemIndex == selectedIndex;

                        var prefix = isSelected ? "> " : "  ";
                        var style = isSelected ? "bold springgreen1" : "white";

                        content.AddRow(
                           $"[{style}]{prefix}{item.Label}[/]"
                        );
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

         var prompt = 
            new MultiSelectionPrompt<EntityMetadata>()
            .Title(
               $"Select {UiMarkup.Green}Entities[/] " +
               "to migrate:"
            )
            .PageSize(15)
            .MoreChoicesText(
               $"{UiMarkup.Grey}" +
               "(Move up and down to reveal more)[/]"
            )
            .InstructionsText(
               $"{UiMarkup.Grey}(Press " +
               $"{UiMarkup.Blue}<space>[/] to toggle, " +
               $"{UiMarkup.Green}<enter>[/] to accept)[/]"
            )
            .UseConverter(e => e.DisplayName.UserLocalizedLabel.Label);

         foreach (var entity in entities)
            prompt.AddChoice(entity);

         var selected = AnsiConsole.Prompt(prompt);

         return selected.Select(e => e.LogicalName).ToList();
      }
   }
}
