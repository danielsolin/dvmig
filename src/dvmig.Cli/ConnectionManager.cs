using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Settings;
using Microsoft.Crm.Sdk.Messages;
using Spectre.Console;

using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli
{
   public class ConnectionManager(ISettingsService settingsService)
   {
      private readonly ISettingsService _settingsService = settingsService;

      private readonly Dictionary<ConnectionDirection, IDataverseProvider>
         _activeConnections = new();

      public IUserService? UserResolver { get; set; }

      public void AddActiveConnection(
         ConnectionDirection direction,
         IDataverseProvider provider
      )
      {
         _activeConnections[direction] = provider;
      }

      public async Task<IDataverseProvider?> ConnectAsync(
         ConnectionDirection direction,
         string? label = null
      )
      {
         label ??= direction.ToString();

         if (_activeConnections.TryGetValue(direction, out var existing))
         {
            var reuse = AnsiConsole.Confirm(
               $"Reuse active connection to {UiMarkup.Green}{label}[/]?",
               true
            );

            if (reuse)
               return existing;

            _activeConnections.Remove(direction);
         }

         var settings = _settingsService.LoadSettings();
         var storedConn = direction == ConnectionDirection.Source
            ? settings.SourceConnectionString
            : settings.TargetConnectionString;

         var connStr = storedConn;
         var isLegacy = false;

         if (!string.IsNullOrEmpty(storedConn))
         {
            var preview = StringMasker.MaskConnectionString(storedConn);
            var useStored = AnsiConsole.Confirm(
               $"Use {UiMarkup.Green}stored[/] {label} connection?",
               true
            );

            if (!useStored)
            {
               connStr = AnsiConsole.Ask<string>(
                  $"Enter {UiMarkup.BoldBlue}{label}[/] " +
                  "Connection String:"
               );

               isLegacy = AnsiConsole.Confirm(
                  $"Is {UiMarkup.BoldBlue}{label}[/] Legacy CRM " +
                  "(OnPrem)?",
                  false
               );
            }
         }
         else
         {
            connStr = AnsiConsole.Ask<string>(
               $"Enter {UiMarkup.BoldBlue}{label}[/] " +
               "Connection String:"
            );

            isLegacy = AnsiConsole.Confirm(
               $"Is {UiMarkup.BoldBlue}{label}[/] Legacy CRM " +
               "(OnPrem)?",
               false
            );
         }

         var provider = await CliUI.RunStatusAsync(
            $"Connecting to {label}...",
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
                  AnsiConsole.MarkupLine(
                     $"{UiMarkup.Red}×[/] Failed to " +
                     $"connect to {label}: {ex.Message}"
                  );

                  return null;
               }
            }
         );

         if (provider != null)
         {
            _activeConnections[direction] = provider;

            // Extract organization URL for a cleaner success message
            var displayInfo = connStr.Contains("://")
               ? connStr.Split("://")[1].Split(";")[0].Split("/")[0]
               : label;

            AnsiConsole.MarkupLine(
               $"{UiMarkup.BoldGreen}✓[/] {label} Connected: " +
               $"{UiMarkup.Grey}{displayInfo}[/]"
            );

            if (connStr != storedConn)
               if (AnsiConsole.Confirm(
                  $"Save this {label} connection string?",
                  true
               ))
               {
                  if (direction == ConnectionDirection.Source)
                     settings.SourceConnectionString = connStr;
                  else
                     settings.TargetConnectionString = connStr;

                  _settingsService.SaveSettings(settings);
               }
         }

         return provider;
      }
   }
}
