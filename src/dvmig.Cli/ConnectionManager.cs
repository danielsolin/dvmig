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

      public IDataverseProvider? GetActiveConnection(ConnectionDirection direction)
      {
         return _activeConnections.TryGetValue(direction, out var provider)
            ? provider
            : null;
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

         var storedIsLegacy = direction == ConnectionDirection.Source
            ? settings.SourceIsLegacy
            : settings.TargetIsLegacy;

         var connStr = storedConn;

         if (!string.IsNullOrEmpty(storedConn))
         {
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
            }
         }
         else
         {
            connStr = AnsiConsole.Ask<string>(
               $"Enter {UiMarkup.BoldBlue}{label}[/] " +
               "Connection String:"
            );
         }

         var provider = await CliUI.RunStatusAsync(
            $"Connecting to {label}...",
            async () =>
            {
               try
               {
                  // If it's the stored connection, we already know if it's 
                  // legacy. Otherwise, we auto-detect.
                  return connStr == storedConn
                     ? await ProviderFactory.CreateAsync(
                        connStr,
                        storedIsLegacy
                     )
                     : await ProviderFactory.CreateAsync(connStr);
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

            if (connStr != storedConn || provider.IsLegacy != storedIsLegacy)
               if (AnsiConsole.Confirm(
                  $"Save this {label} connection string?",
                  true
               ))
               {
                  if (direction == ConnectionDirection.Source)
                  {
                     settings.SourceConnectionString = connStr;
                     settings.SourceIsLegacy = provider.IsLegacy;
                  }
                  else
                  {
                     settings.TargetConnectionString = connStr;
                     settings.TargetIsLegacy = provider.IsLegacy;
                  }

                  _settingsService.SaveSettings(settings);
               }
         }

         return provider;
      }
   }
}
