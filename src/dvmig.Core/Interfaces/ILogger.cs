namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Defines the contract for a logger that supports both standard logging
   /// and progress reporting to the user interface.
   /// </summary>
   public interface ILogger
   {
      /// <summary>
      /// Logs a debug-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Debug(string message);

      /// <summary>
      /// Logs a debug-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Debug(string messageTemplate, params object[] propertyValues);
      
      /// <summary>
      /// Logs an information-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Information(string message);

      /// <summary>
      /// Logs an information-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Information(string messageTemplate, params object[] propertyValues);
      
      /// <summary>
      /// Logs a warning-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Warning(string message);

      /// <summary>
      /// Logs a warning-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Warning(string messageTemplate, params object[] propertyValues);

      /// <summary>
      /// Logs a warning-level message with an exception.
      /// </summary>
      /// <param name="ex">The exception to log.</param>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Warning(
         Exception ex, 
         string messageTemplate, 
         params object[] propertyValues
      );
      
      /// <summary>
      /// Logs an error-level message.
      /// </summary>
      /// <param name="message">The message to log.</param>
      void Error(string message);

      /// <summary>
      /// Logs an error-level message using a template.
      /// </summary>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Error(string messageTemplate, params object[] propertyValues);

      /// <summary>
      /// Logs an error-level message with an exception.
      /// </summary>
      /// <param name="ex">The exception to log.</param>
      /// <param name="messageTemplate">The message template.</param>
      /// <param name="propertyValues">The values to format.</param>
      void Error(
         Exception ex, 
         string messageTemplate, 
         params object[] propertyValues
      );
      
      /// <summary>
      /// Attaches a progress reporter to the logger.
      /// </summary>
      /// <param name="progress">The progress reporter.</param>
      void AttachProgress(IProgress<string> progress);

      /// <summary>
      /// Detaches the progress reporter from the logger.
      /// </summary>
      void DetachProgress();
   }
}
