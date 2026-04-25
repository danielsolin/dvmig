namespace dvmig.Shared.Metadata
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
    }
}
