#if !NETSTANDARD2_0
#pragma warning disable IDE0005 // Using directive is unnecessary.
using System;
#pragma warning restore IDE0005 // Using directive is unnecessary.
using System.Text.RegularExpressions;
using Serilog;
using SerilogLogger = Serilog.ILogger;

namespace dvmig.Core.Shared
{
   public class Logger : Interfaces.ILogger
   {
      private readonly SerilogLogger _serilog;
      private IProgress<string>? _progress;

      public Logger()
      {
         var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData
         );

         var logPath = Path.Combine(
            appData,
            SystemConstants.AppConstants.AppName,
            "logs",
            $"{SystemConstants.AppConstants.AppName}.log"
         );

         _serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
               logPath,
               rollingInterval: RollingInterval.Day,
               flushToDiskInterval: TimeSpan.FromSeconds(1),
               outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                  "[{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
      }

      public void AttachProgress(IProgress<string> progress)
      {
         _progress = progress;
      }

      public void DetachProgress()
      {
         _progress = null;
      }

      public void Debug(string message)
      {
         _serilog.Debug(message);
         // Debug messages usually don't go to progress
      }

      public void Debug(string messageTemplate, params object[] propertyValues)
      {
         _serilog.Debug(messageTemplate, propertyValues);
      }

      public void Information(string message)
      {
         _serilog.Information(message);
         _progress?.Report(message);
      }

      public void Information(
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Information(messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      public void Warning(string message)
      {
         _serilog.Warning(message);
         _progress?.Report(message);
      }

      public void Warning(
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Warning(messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      public void Warning(
         Exception ex, 
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Warning(ex, messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      public void Error(string message)
      {
         _serilog.Error(message);
         _progress?.Report(message);
      }

      public void Error(
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Error(messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      public void Error(
         Exception ex, 
         string messageTemplate, 
         params object[] propertyValues
      )
      {
         _serilog.Error(ex, messageTemplate, propertyValues);
         ReportWithFormatting(messageTemplate, propertyValues);
      }

      private void ReportWithFormatting(
         string messageTemplate, 
         object[] propertyValues
      )
      {
         if (_progress == null)
            return;

         try
         {
            if (propertyValues == null || propertyValues.Length == 0)
            {
               _progress.Report(messageTemplate);

               return;
            }

            int index = 0;
            var formattedTemplate = Regex.Replace(
               messageTemplate, 
               @"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", 
               m => $"{{{index++}}}"
            );

            var formatted = string.Format(formattedTemplate, propertyValues);
            _progress.Report(formatted);
         }
         catch
         {
            _progress.Report(messageTemplate);
         }
      }
   }
}
#endif
