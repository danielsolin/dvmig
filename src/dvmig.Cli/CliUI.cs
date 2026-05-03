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
      /// Runs an asynchronous task with a spinning status indicator and 
      /// provides a progress reporter that logs to the console.
      /// </summary>
      /// <param name="statusMessage">The message to display.</param>
      /// <param name="action">The asynchronous action to execute.</param>
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
                  AnsiConsole.MarkupLine(
                     $"{SystemConstants.UiMarkup.Grey}" +
                     $"[[{DateTime.Now:HH:mm:ss}]][/] {msg}"
                  );
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
         AnsiConsole.MarkupLine(
            "[bold]Dataverse Migrator - Technical UI[/]"
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
            "Press any key to return to menu...[/]"
         );

         Console.ReadKey(true);
      }

      public static async Task<List<string>?> SelectEntitiesAsync(
         IMetadataService metadataService,
         IDataverseProvider provider
      )
      {
         var entities = await RunStatusAsync(
            "Fetching entity metadata...",
            async () =>
            {
               try
               {
                  return await metadataService
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
