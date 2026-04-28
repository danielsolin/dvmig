namespace dvmig.Core.Shared
{
   /// <summary>
   /// Centralized repository for custom Dataverse entity and attribute names 
   /// used by the dvmig tool.
   /// </summary>
   public static class SystemConstants
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
         public const string EntityLogicalNameAttr = "dm_" +
             "sourceentitylogicalname";
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
      /// Common Dataverse attribute names.
      /// </summary>
      public static class DataverseAttributes
      {
         public const string StateCode = "statecode";
         public const string StatusCode = "statuscode";
         public const string CreatedOn = "createdon";
         public const string ModifiedOn = "modifiedon";
         public const string Count = "count";
         public const string InternalEmailAddress = "internalemailaddress";
         public const string DomainName = "domainname";
      }

      /// <summary>
      /// Keywords found in Dataverse error messages for pattern matching.
      /// </summary>
      public static class ErrorKeywords
      {
         public const string AlreadyExists = "already exists";
         public const string DuplicateCurrency = "duplicate currency record";
         public const string DuplicateKey = "duplicate key";
         public const string InvalidStatusCode = "is not a valid status code";
         public const string DoesNotExist = "does not exist";
         public const string ForeignKeyConflict = "conflicted with the " +
             "foreign key constraint";
         public const string ConstraintConflict = "conflicted with a " +
             "constraint";
         public const string CannotBeModified = "cannot be modified";
         public const string CannotBeSetOnCreation = "cannot be set on " +
             "creation";
         public const string OutsideValidRange = "outside the valid range";
         public const string TooManyRequests = "too many requests";
         public const string CombinedExecutionTime = "combined execution time";
         public const string GenericSqlError = "generic sql error";
         public const string Timeout = "timeout";
      }

      /// <summary>
      /// Specific Dataverse error codes.
      /// </summary>
      public static class ErrorCodes
      {
         public const string ServiceProtectionLimit = "8004410d";
         public const string ConnectionTimeout = "80072321";
         public const string DoesNotExist = "80040217";
      }

      /// <summary>
      /// Keywords used to identify sensitive information for masking.
      /// </summary>
      public static class MaskingKeywords
      {
         public const string Password = "password";
         public const string Secret = "secret";
         public const string Token = "token";
         public const string Thumbprint = "thumbprint";
         public const string ClientId = "clientid";
         public const string AppId = "appid";
         public const string UserId = "userid";
         public const string Username = "username";
      }

      /// <summary>
      /// Constants for CLI arguments and environment settings.
      /// </summary>
      public static class CliSettings
      {
         public const string DevShort = "-dev";
         public const string DevLong = "--dev";
         public const string DevFull = "--developer-mode";
         public const string AuthAd = "AuthType=AD";
         public const string AuthIfd = "AuthType=IFD";
      }

      /// <summary>
      /// Spectre.Console markup and UI keywords.
      /// </summary>
      public static class UiMarkup
      {
         public const string Yellow = "[yellow]";
         public const string Red = "[red]";
         public const string Grey = "[grey]";
         public const string Wait = "WAIT";
      }

      /// <summary>
      /// Application-wide constants for configuration, batch sizes, 
      /// and file names.
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
