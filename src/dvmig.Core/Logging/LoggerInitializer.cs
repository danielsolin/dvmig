using Serilog;

namespace dvmig.Core.Logging
{
    /// <summary>
    /// Provides methods to initialize and configure logging for the application.
    /// </summary>
    public static class LoggerInitializer
    {
        /// <summary>
        /// Configures the global Serilog logger to write to both the debug 
        /// output and a rolling file in the user's AppData directory.
        /// </summary>
        /// <param name="appName">
        /// The name of the application (used for the log file name).
        /// </param>
        /// <returns>The configured ILogger instance.</returns>
        public static ILogger Initialize(string appName)
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            );
            
            var logPath = Path.Combine(appData, "dvmig", "logs", $"{appName}.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            return Log.Logger;
        }
    }
}
