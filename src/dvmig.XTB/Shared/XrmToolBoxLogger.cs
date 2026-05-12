using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using XrmToolBox.Extensibility;
using dvmig.Core.Interfaces;

namespace dvmig.XTB.Shared
{
   /// <summary>
   /// Logger implementation for XrmToolBox that avoids using Serilog to 
   /// prevent assembly version conflicts in the XrmToolBox environment.
   /// Logs to Debug output and the provided progress reporter.
   /// </summary>
   public class XrmToolBoxLogger : ILogger
   {
      private readonly PluginControlBase _control;
      private IProgress<string>? _progress;

      public XrmToolBoxLogger(PluginControlBase control)
      {
         _control = control;
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
         System.Diagnostics.Debug.WriteLine($"DEBUG: {message}");
      }

      public void Debug(string messageTemplate, params object[] propertyValues)
      {
         System.Diagnostics.Debug.WriteLine(
            $"DEBUG: {Format(messageTemplate, propertyValues)}"
         );
      }

      public void Information(string message)
      {
         System.Diagnostics.Debug.WriteLine($"INFO: {message}");
         _progress?.Report(message);
      }

      public void Information(
         string messageTemplate, params object[] propertyValues
      )
      {
         var formatted = Format(messageTemplate, propertyValues);
         System.Diagnostics.Debug.WriteLine($"INFO: {formatted}");
         _progress?.Report(formatted);
      }

      public void Warning(string message)
      {
         System.Diagnostics.Debug.WriteLine($"WARN: {message}");
         _progress?.Report($"[WARN] {message}");
      }

      public void Warning(
         string messageTemplate, params object[] propertyValues
      )
      {
         var formatted = Format(messageTemplate, propertyValues);
         System.Diagnostics.Debug.WriteLine($"WARN: {formatted}");
         _progress?.Report($"[WARN] {formatted}");
      }

      public void Warning(
         Exception ex, string messageTemplate, params object[] propertyValues
      )
      {
         var formatted = Format(messageTemplate, propertyValues);
         System.Diagnostics.Debug.WriteLine(
            $"WARN: {formatted}. Exception: {ex}"
         );
         _progress?.Report($"[WARN] {formatted}: {ex.Message}");
      }

      public void Error(string message)
      {
         System.Diagnostics.Debug.WriteLine($"ERROR: {message}");
         _progress?.Report($"[ERROR] {message}");
      }

      public void Error(string messageTemplate, params object[] propertyValues)
      {
         var formatted = Format(messageTemplate, propertyValues);
         System.Diagnostics.Debug.WriteLine($"ERROR: {formatted}");
         _progress?.Report($"[ERROR] {formatted}");
      }

      public void Error(
         Exception ex,
         string messageTemplate,
         params object[] propertyValues
      )
      {
         var formatted = Format(messageTemplate, propertyValues);
         System.Diagnostics.Debug.WriteLine(
            $"ERROR: {formatted}. Exception: {ex}"
         );
         _progress?.Report($"[ERROR] {formatted}: {ex.Message}");
      }

      private string Format(string messageTemplate, object[] propertyValues)
      {
         try
         {
            if (propertyValues == null || propertyValues.Length == 0)
               return messageTemplate;

            int index = 0;
            var formattedTemplate = Regex.Replace(
               messageTemplate, 
               @"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", 
               m => $"{{{index++}}}"
            );

            return string.Format(formattedTemplate, propertyValues);
         }
         catch
         {
            return messageTemplate;
         }
      }
   }
}
