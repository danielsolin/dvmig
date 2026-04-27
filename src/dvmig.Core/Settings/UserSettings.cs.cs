namespace dvmig.Core.Settings
{
    /// <summary>
    /// Represents the persisted user settings for the application.
    /// </summary>
    public class UserSettings
    {
        /// <summary>
        /// Gets or sets the encrypted connection string for the source 
        /// environment.
        /// </summary>
        public string SourceConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the encrypted connection string for the target 
        /// environment.
        /// </summary>
        public string TargetConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether connection strings 
        /// should be persisted.
        /// </summary>
        public bool RememberConnections { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to automatically attempt 
        /// connection on startup.
        /// </summary>
        public bool AutoConnect { get; set; }
    }
}
