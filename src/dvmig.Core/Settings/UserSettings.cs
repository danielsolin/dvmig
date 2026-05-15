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
      /// Gets or sets the preferred language code (e.g., "en", "sv").
      /// </summary>
      public string Language { get; set; } = "en";

      /// <summary>
      /// Gets or sets the maximum number of parallel sync threads.
      /// </summary>
      public int MaxParallelism { get; set; } = 10;

      /// <summary>
      /// Gets or sets a value indicating whether to automatically create 
      /// missing related records.
      /// </summary>
      public bool AutoCreateRelatedRecords { get; set; } = true;

      /// <summary>
      /// Gets or sets a value indicating whether the source environment
      /// is a legacy (OnPrem) environment.
      /// </summary>
      public bool SourceIsLegacy { get; set; }

      /// <summary>
      /// Gets or sets a value indicating whether the target environment
      /// is a legacy (OnPrem) environment.
      /// </summary>
      public bool TargetIsLegacy { get; set; }
   }
}
