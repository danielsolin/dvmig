using System;
using System.Text.RegularExpressions;
using Serilog;

namespace dvmig.Core.Logging
{
   /// <summary>
   /// Extension methods for <see cref="ILogger"/> to support simultaneous 
   /// logging and progress reporting.
   /// </summary>
   public static class LoggerExtensions
   {
      /// <summary>
      /// Logs an information message and reports it to the progress indicator.
      /// </summary>
      public static void Information(
         this ILogger logger,
         IProgress<string>? progress,
         string message
      )
      {
         logger.Information(message);
         progress?.Report(message);
      }

      /// <summary>
      /// Logs an information message with parameters and reports it 
      /// to the progress indicator.
      /// </summary>
      public static void Information(
         this ILogger logger,
         IProgress<string>? progress,
         string messageTemplate,
         params object[] propertyValues
      )
      {
         logger.Information(messageTemplate, propertyValues);
         ReportWithFormatting(progress, messageTemplate, propertyValues);
      }

      /// <summary>
      /// Logs a warning message and reports it to the progress indicator.
      /// </summary>
      public static void Warning(
         this ILogger logger,
         IProgress<string>? progress,
         string message
      )
      {
         logger.Warning(message);
         progress?.Report(message);
      }

      /// <summary>
      /// Logs a warning message with parameters and reports it 
      /// to the progress indicator.
      /// </summary>
      public static void Warning(
         this ILogger logger,
         IProgress<string>? progress,
         string messageTemplate,
         params object[] propertyValues
      )
      {
         logger.Warning(messageTemplate, propertyValues);
         ReportWithFormatting(progress, messageTemplate, propertyValues);
      }

      /// <summary>
      /// Logs an error message and reports it to the progress indicator.
      /// </summary>
      public static void Error(
         this ILogger logger,
         IProgress<string>? progress,
         string message
      )
      {
         logger.Error(message);
         progress?.Report(message);
      }

      /// <summary>
      /// Logs an error message with parameters and reports it 
      /// to the progress indicator.
      /// </summary>
      public static void Error(
         this ILogger logger,
         IProgress<string>? progress,
         string messageTemplate,
         params object[] propertyValues
      )
      {
         logger.Error(messageTemplate, propertyValues);
         ReportWithFormatting(progress, messageTemplate, propertyValues);
      }

      private static void ReportWithFormatting(
         IProgress<string>? progress,
         string messageTemplate,
         object[] propertyValues
      )
      {
         if (progress == null) return;

         try
         {
            if (propertyValues == null || propertyValues.Length == 0)
            {
               progress.Report(messageTemplate);
               return;
            }

            int index = 0;
            var formattedTemplate = Regex.Replace(
               messageTemplate, 
               @"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", 
               m => $"{{{index++}}}"
            );

            var formatted = string.Format(formattedTemplate, propertyValues);
            progress.Report(formatted);
         }
         catch
         {
            progress.Report(messageTemplate);
         }
      }
   }
}
