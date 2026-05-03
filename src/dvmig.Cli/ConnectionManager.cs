using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Settings;
using dvmig.Core.Shared;
using Microsoft.Crm.Sdk.Messages;
using Spectre.Console;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Cli
{
   public class ConnectionManager
   {
      private readonly ISettingsService _settingsService;

      private readonly Dictionary<ConnectionDirection, IDataverseProvider>
         _activeConnections = new();

      public ConnectionManager(ISettingsService settingsService)
      {
         _settingsService = settingsService;
      }

      public async Task<IDataverseProvider?> ConnectAsync(
         ConnectionDirection direction,
         string? label = null
      )
      {
         label ??= direction.ToString();

         if (_activeConnections.TryGetValue(direction, out var existing))
         {
            var msg =
               $"An active connection to {SystemConstants.UiMarkup.Green}" +
               $"{label}[/] already exists. Reuse it?";

            var reuse = AnsiConsole.Confirm(msg, true);

            if (reuse)
               return existing;

            _activeConnections.Remove(direction);
         }

         var settings = _settingsService.LoadSettings();

         string? storedConn = direction == ConnectionDirection.Source
            ? settings.SourceConnectionString
            : settings.TargetConnectionString;

         string connStr;

         if (!string.IsNullOrEmpty(storedConn))
         {
            var preview = StringMasker.MaskConnectionString(storedConn);

            var confirmMsg =
               $"Use {SystemConstants.UiMarkup.Green}stored[/] {label} " +
               $"connection string?\n{SystemConstants.UiMarkup.Grey}" +
               $"({preview})[/]";

            var useStored = AnsiConsole.Confirm(confirmMsg, true);

            connStr = useStored
               ? storedConn
               : AnsiConsole.Ask<string>(
                  $"Enter {SystemConstants.UiMarkup.BoldBlue}{label}[/] " +
                  "Connection String:"
               );
         }
         else
         {
            connStr = AnsiConsole.Ask<string>(
               $"Enter {SystemConstants.UiMarkup.BoldBlue}{label}[/] " +
               "Connection String:"
            );
         }

         var isLegacy = AnsiConsole.Confirm(
            $"Is {SystemConstants.UiMarkup.BoldBlue}{label}[/] Legacy CRM " +
            "(OnPrem)?",
            false
         );

         IDataverseProvider? provider = await CliUI.RunStatusAsync(
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
                     $"{SystemConstants.UiMarkup.Red}×[/] Failed to " +
                     $"connect to {label}: {ex.Message}"
                  );

                  return null;
               }
            }
         );

         if (provider != null)
         {
            _activeConnections[direction] = provider;

            CliUI.WriteSuccess($"Connected to {label}");

            if (connStr != storedConn)
            {
               var savePrompt = $"Save this {label} connection string " +
                                "for future use?";

               if (AnsiConsole.Confirm(savePrompt, true))
               {
                  settings.RememberConnections = true;

                  if (direction == ConnectionDirection.Source)
                     settings.SourceConnectionString = connStr;
                  else
                     settings.TargetConnectionString = connStr;

                  _settingsService.SaveSettings(settings);

                  AnsiConsole.MarkupLine(
                     $"{SystemConstants.UiMarkup.Grey}Settings saved.[/]"
                  );
               }
            }
         }

         return provider;
      }
   }
}
