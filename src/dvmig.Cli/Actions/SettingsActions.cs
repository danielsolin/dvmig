using Microsoft.Crm.Sdk.Messages;

using Spectre.Console;

using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Settings;
using dvmig.Core.Shared;

namespace dvmig.Cli.Actions
{
   /// <summary>
   /// Handles application settings management from the CLI.
   /// </summary>
   public class SettingsActions
   {
      private readonly ISettingsService _settingsService;
      private readonly ConnectionManager _connectionManager;

      public SettingsActions(
         ISettingsService settingsService,
         ConnectionManager connectionManager
      )
      {
         _settingsService = settingsService;
         _connectionManager = connectionManager;
      }

      /// <summary>
      /// Displays the settings menu and handles user interaction.
      /// </summary>
      public async Task HandleSettingsMenuAsync(CancellationToken ct)
      {
         bool back = false;

         while (!back)
         {
            CliUI.WriteHeader();

            var settings = _settingsService.LoadSettings();

            var prompt = new SelectionPrompt<string>()
               .Title("Settings".t())
               .PageSize(10)
               .AddChoices(
                  new[]
                  {
                     $"{"Language".t()}: " + 
                     $"{GetCurrentLanguageName(settings.Language)}",
                     $"{"Source Connection String".t()}: " + 
                     $"{StringMasker.GetEnvironmentUrl(
                        settings.SourceConnectionString)}",
                     $"{"Target Connection String".t()}: " + 
                     $"{StringMasker.GetEnvironmentUrl(
                        settings.TargetConnectionString)}",
                     $"{"Remember Connections".t()}: " + 
                     $"{(settings.RememberConnections ? "Yes".t() : "No".t())}",
                     $"{"Auto Connect".t()}: " + 
                     $"{(settings.AutoConnect ? "Yes".t() : "No".t())}",
                     "Back".t()
                  }
               );

            var choice = AnsiConsole.Prompt(prompt);

            if (choice == "Back".t())
            {
               back = true;
            }
            else if (choice.StartsWith("Language".t()))
            {
               await HandleLanguageChangeAsync(settings);
            }
            else if (choice.StartsWith("Source Connection String".t()))
            {
               await HandleConnectionStringChange(
                  settings, 
                  SystemConstants.ConnectionDirection.Source
               );
            }
            else if (choice.StartsWith("Target Connection String".t()))
            {
               await HandleConnectionStringChange(
                  settings, 
                  SystemConstants.ConnectionDirection.Target
               );
            }
            else if (choice.StartsWith("Remember Connections".t()))
            {
               settings.RememberConnections = !settings.RememberConnections;
               _settingsService.SaveSettings(settings);
            }
            else if (choice.StartsWith("Auto Connect".t()))
            {
               settings.AutoConnect = !settings.AutoConnect;
               _settingsService.SaveSettings(settings);
            }
         }
      }

      private string GetCurrentLanguageName(string code)
      {
         return code.ToLowerInvariant() switch
         {
            "sv" => "Swedish".t(),
            _ => "English".t()
         };
      }

      private async Task HandleLanguageChangeAsync(UserSettings settings)
      {
         var prompt = new SelectionPrompt<string>()
            .Title("Select Language".t())
            .AddChoices(
               new[]
               {
                  "English".t(),
                  "Swedish".t()
               }
            );

         var choice = AnsiConsole.Prompt(prompt);
         var newLanguage = choice == "Swedish".t() ? "sv" : "en";

         if (settings.Language != newLanguage)
         {
            settings.Language = newLanguage;
            _settingsService.SaveSettings(settings);
            LocalizationService.Initialize(newLanguage);
            
            AnsiConsole.MarkupLine(
               $"{SystemConstants.UiMarkup.Green}" + 
               $"{"Settings updated.".t()}[/]"
            );

            await Task.Delay(1000);
         }
      }

      private async Task HandleConnectionStringChange(
         UserSettings settings, 
         SystemConstants.ConnectionDirection direction
      )
      {
         var label = direction == SystemConstants.ConnectionDirection.Source 
            ? "Source".t() 
            : "Target".t();
         
         var current = direction == SystemConstants.ConnectionDirection.Source 
            ? settings.SourceConnectionString 
            : settings.TargetConnectionString;

         bool back = false;

         while (!back)
         {
            CliUI.WriteHeader();
            
            AnsiConsole.MarkupLine(
               $"[bold]{"Edit {0} Connection String".t(label)}[/]"
            );

            AnsiConsole.WriteLine();

            var prompt = new SelectionPrompt<string>()
               .AddChoices(
                  new[]
                  {
                     $"{"Connection String:".t()} " + 
                     $"{StringMasker.GetEnvironmentUrl(current)}",
                     "Test Connection".t(),
                     "Back".t()
                  }
               );

            var choice = AnsiConsole.Prompt(prompt);

            if (choice == "Back".t())
            {
               back = true;
            }
            else if (choice.StartsWith("Connection String:".t()))
            {
               var newConn = AnsiConsole.Prompt(
                  new TextPrompt<string>("Connection String:".t())
                     .DefaultValue(current)
                     .HideDefaultValue()
               );

               if (!string.IsNullOrWhiteSpace(newConn) && newConn != current)
               {
                  if (direction == SystemConstants.ConnectionDirection.Source)
                     settings.SourceConnectionString = newConn;
                  else
                     settings.TargetConnectionString = newConn;

                  _settingsService.SaveSettings(settings);
                  current = newConn;

                  AnsiConsole.MarkupLine(
                     $"{SystemConstants.UiMarkup.Green}" + 
                     $"{"Settings updated.".t()}[/]"
                  );
                  
                  await Task.Delay(1000);
               }
            }
            else if (choice == "Test Connection".t())
            {
               await HandleTestConnectionAsync(current, direction);
            }
         }
      }

      private async Task HandleTestConnectionAsync(
         string connStr, 
         SystemConstants.ConnectionDirection direction
      )
      {
         if (string.IsNullOrWhiteSpace(connStr))
            return;

         bool isLegacy = AnsiConsole.Confirm(
            "Is this a Legacy CRM (OnPrem) environment?".t(),
            false
         );

         IDataverseProvider? provider = await CliUI.RunStatusAsync(
            "Testing connection...".t(),
            async () =>
            {
               try
               {
                  IDataverseProvider p = isLegacy
                     ? new LegacyCrmProvider(connStr)
                     : new DataverseProvider(connStr);

                  await p.ExecuteAsync(new WhoAmIRequest(), default);

                  return p;
               }
               catch (Exception ex)
               {
                  CliUI.WriteError(
                     "Connection failed: {0}".t(ex.GetBaseException().Message)
                  );

                  return null;
               }
            }
         );

         if (provider != null)
         {
            _connectionManager.AddActiveConnection(direction, provider);

            CliUI.WriteSuccess("Connection successful!".t());
            await Task.Delay(1500);
         }
         else
         {
         }
      }
   }
}
