namespace dvmig.Core.Shared
{
    /// <summary>
    /// Centralized repository for custom Dataverse entity and attribute names 
    /// used by the dvmig tool.
    /// </summary>
    public static class SchemaConstants
    {
        /// <summary>
        /// Constants for the date preservation entity.
        /// </summary>
        public static class SourceDate
        {
            public const string EntityLogicalName = "dm_sourcedate";
            public const string PrimaryId = "dm_sourcedateid";
            public const string Name = "dm_name";
            public const string EntityId = "dm_sourceentityid";
            public const string EntityLogicalNameAttr = "dm_sourceentitylogicalname";
            public const string CreatedDate = "dm_sourcecreateddate";
            public const string ModifiedDate = "dm_sourcemodifieddate";
        }

        /// <summary>
        /// Constants for the migration failure logging entity.
        /// </summary>
        public static class MigrationFailure
        {
            public const string EntityLogicalName = "dm_migrationfailure";
            public const string PrimaryId = "dm_migrationfailureid";
            public const string Name = "dm_name";
            public const string SourceId = "dm_sourceid";
            public const string EntityLogicalNameAttr = "dm_entitylogicalname";
            public const string ErrorMessage = "dm_errormessage";
            public const string Timestamp = "dm_timestamp";
        }

        /// <summary>
        /// Application-wide constants for configuration, batch sizes, and file names.
        /// </summary>
        public static class AppConstants
        {
            public const int DefaultDeletionBatchSize = 5000;
            public const int MetadataPropagationDelayMs = 5000;
            public const int MaxMemoFieldLength = 5000;
            public const int RecordFetchLimit = 100;
            public const string PluginAssemblyName = "dvmig.Plugins.dll";
            public const string PluginName = "dvmig.Plugins";
        }
    }
}
