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
   public class SettingsActions(
      ISettingsService settingsService,
      ConnectionManager connectionManager
   )
   {
      private readonly ISettingsService _settingsService = settingsService;
      private readonly ConnectionManager _connectionManager = connectionManager;

      private enum SettingChoice
      {
         SourceConn,
         TargetConn,
         MaxThreads,
         Language,
         Back
      }

      private enum ConnectionSettingChoice
      {
         EditConn,
         TestConn,
         Back
      }

      private enum LanguageChoice
      {
         English,
         Swedish
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

            var prompt = new SelectionPrompt<SettingChoice>()
               .Title("Settings".t())
               .PageSize(8)
               .UseConverter(c => c switch
               {
                  SettingChoice.SourceConn =>
                     $"{"Source Connection String".t()}: " + 
                     $"{StringMasker.GetEnvironmentUrl(
                        settings.SourceConnectionString)}",
                  SettingChoice.TargetConn =>
                     $"{"Target Connection String".t()}: " + 
                     $"{StringMasker.GetEnvironmentUrl(
                        settings.TargetConnectionString)}",
                  SettingChoice.MaxThreads =>
                     $"{"Max Threads".t()}: {settings.MaxParallelism}",
                  SettingChoice.Language =>
                     $"{"Language".t()}: " +
                     $"{GetCurrentLanguageName(settings.Language)}",
                  SettingChoice.Back => "Back".t(),
                  _ => throw new ArgumentOutOfRangeException()
               })
               .AddChoices(Enum.GetValues<SettingChoice>());

            var choice = AnsiConsole.Prompt(prompt);

            switch (choice)
            {
               case SettingChoice.Back:
                  back = true;
                  break;
               case SettingChoice.Language:
                  await HandleLanguageChangeAsync(settings);
                  break;
               case SettingChoice.SourceConn:
                  await HandleConnectionStringChange(
                     settings, 
                     SystemConstants.ConnectionDirection.Source
                  );
                  break;
               case SettingChoice.TargetConn:
                  await HandleConnectionStringChange(
                     settings, 
                     SystemConstants.ConnectionDirection.Target
                  );
                  break;
               case SettingChoice.MaxThreads:
                  await HandleMaxParallelismChangeAsync(settings);
                  break;
            }
         }
      }

      private static string GetCurrentLanguageName(string code)
      {
         return code.ToLowerInvariant() switch
         {
            "sv" => "Swedish".t(),
            _ => "English".t()
         };
      }

      private async Task HandleLanguageChangeAsync(UserSettings settings)
      {
         var prompt = new SelectionPrompt<LanguageChoice>()
            .Title("Select Language".t())
            .UseConverter(c => c switch
            {
               LanguageChoice.English => "English".t(),
               LanguageChoice.Swedish => "Swedish".t(),
               _ => throw new ArgumentOutOfRangeException()
            })
            .AddChoices(Enum.GetValues<LanguageChoice>());

         var choice = AnsiConsole.Prompt(prompt);
         var newLanguage = choice == LanguageChoice.Swedish ? "sv" : "en";

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

      private async Task HandleMaxParallelismChangeAsync(UserSettings settings)
      {
         var maxThreads = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
               .Title(
                  $"Select {SystemConstants.UiMarkup.Green}Max Parallelism[/]"
                  + $" ({"Threads".t()}):"
               )
               .AddChoices(SystemConstants.SyncSettings.ParallelismOptions)
         );

         if (settings.MaxParallelism != maxThreads)
         {
            settings.MaxParallelism = maxThreads;
            _settingsService.SaveSettings(settings);

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

            var prompt = new SelectionPrompt<ConnectionSettingChoice>()
               .UseConverter(c => c switch
               {
                  ConnectionSettingChoice.EditConn =>
                     $"{"Connection String:".t()} " + 
                     $"{StringMasker.GetEnvironmentUrl(current)}",
                  ConnectionSettingChoice.TestConn => "Test Connection".t(),
                  ConnectionSettingChoice.Back => "Back".t(),
                  _ => throw new ArgumentOutOfRangeException()
               })
               .AddChoices(Enum.GetValues<ConnectionSettingChoice>());

            var choice = AnsiConsole.Prompt(prompt);

            switch (choice)
            {
               case ConnectionSettingChoice.Back:
                  back = true;
                  break;
               case ConnectionSettingChoice.EditConn:
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
                  break;
               case ConnectionSettingChoice.TestConn:
                  await HandleTestConnectionAsync(current, direction);
                  break;
            }
         }
      }

      private async Task HandleTestConnectionAsync(
         string connStr, 
         SystemConstants.ConnectionDirection direction
      )
      {
         if (string.IsNullOrWhiteSpace(connStr))
         {
            CliUI.WriteError("Connection string is empty.".t());
            await Task.Delay(1000);

            return;
         }

         if (_connectionManager == null)
         {
            CliUI.WriteError("CRITICAL: _connectionManager is null!");
            CliUI.Pause();

            return;
         }

         bool isLegacy = AnsiConsole.Confirm(
            "Is this a Legacy CRM (OnPrem) environment?".t(),
            false
         );

         IDataverseProvider? provider = null;
         Exception? caughtException = null;

         // We run the connection logic OUTSIDE the RunStatusAsync first
         // to see if it's the spinner itself causing the vanishing.
         await AnsiConsole.Status().StartAsync("Testing connection...".t(),
            async ctx =>
            {
               try
               {
                  provider = isLegacy
                     ? new LegacyCrmProvider(connStr)
                     : new DataverseProvider(connStr);

                  await provider.ExecuteAsync(new WhoAmIRequest(), default);
               }
               catch (Exception ex)
               {
                  caughtException = ex;
                  provider = null;
               }
            });

         if (caughtException != null)
         {
            AnsiConsole.WriteLine();
            CliUI.WriteError(
               "Connection failed: {0}".t(
                  caughtException.GetBaseException().Message
               )
            );
            
            CliUI.Pause();

            return;
         }

         if (provider != null)
         {
            try
            {
               _connectionManager.AddActiveConnection(direction, provider);

               CliUI.WriteSuccess("Connection successful!".t());
               await Task.Delay(1500);
            }
            catch (Exception ex)
            {
               CliUI.WriteError($"Failed to register connection: {ex.Message}");
               CliUI.Pause();
            }
         }
         else
         {
            CliUI.WriteError(
               "Unknown error: Provider is null but no exception was caught."
            );
            CliUI.Pause();
         }
      }
   }
}
