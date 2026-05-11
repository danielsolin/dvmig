using System;
using XrmToolBox.Extensibility;
using dvmig.Core.Interfaces;

namespace dvmig.XTB.Shared
{
   /// <summary>
   /// Logger implementation for XrmToolBox that wraps the core logger 
   /// and provides integration with the XrmToolBox UI.
   /// </summary>
   public class XrmToolBoxLogger : ILogger
   {
      private readonly Core.Shared.Logger _coreLogger;
      private readonly PluginControlBase _control;

      public XrmToolBoxLogger(PluginControlBase control)
      {
         _coreLogger = new Core.Shared.Logger();
         _control = control;
      }

      public void Debug(string message) => _coreLogger.Debug(message);

      public void Debug(
         string messageTemplate,
         params object[] propertyValues
      ) => _coreLogger.Debug(messageTemplate, propertyValues);
      
      public void Information(string message)
      {
         _coreLogger.Information(message);
         // Optionally update XTB status bar or log
      }

      public void Information(
         string messageTemplate,
         params object[] propertyValues
      )
      {
         _coreLogger.Information(messageTemplate, propertyValues);
      }

      public void Warning(string message) => _coreLogger.Warning(message);

      public void Warning(
         string messageTemplate,
         params object[] propertyValues
      ) => _coreLogger.Warning(messageTemplate, propertyValues);

      public void Warning(
         Exception ex,
         string messageTemplate,
         params object[] propertyValues
      ) => _coreLogger.Warning(ex, messageTemplate, propertyValues);

      public void Error(string message) => _coreLogger.Error(message);

      public void Error(
         string messageTemplate,
         params object[] propertyValues
      ) => _coreLogger.Error(messageTemplate, propertyValues);

      public void Error(
         Exception ex,
         string messageTemplate,
         params object[] propertyValues
      ) => _coreLogger.Error(ex, messageTemplate, propertyValues);

      public void AttachProgress(
         IProgress<string> progress
      ) => _coreLogger.AttachProgress(progress);

      public void DetachProgress() => _coreLogger.DetachProgress();
   }
}
