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

      private enum SettingChoice
      {
         SourceConn,
         TargetConn,
         RememberConn,
         AutoConnect,
         MaxThreads,
         Language,
         Back
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
               .PageSize(10)
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
                  SettingChoice.RememberConn =>
                     $"{"Remember Connections".t()}: " + 
                     $"{(settings.RememberConnections ? "Yes".t() : "No".t())}",
                  SettingChoice.AutoConnect =>
                     $"{"Auto Connect".t()}: " + 
                     $"{(settings.AutoConnect ? "Yes".t() : "No".t())}",
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
               case SettingChoice.RememberConn:
                  settings.RememberConnections = !settings.RememberConnections;
                  _settingsService.SaveSettings(settings);
                  break;
               case SettingChoice.AutoConnect:
                  settings.AutoConnect = !settings.AutoConnect;
                  _settingsService.SaveSettings(settings);
                  break;
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
