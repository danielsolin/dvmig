using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace dvmig.Core.Shared
{
   /// <summary>
   /// Centralized repository for custom Dataverse entity and attribute names 
   /// used by the dvmig tool.
   /// </summary>
   public static class SystemConstants
   {
      public enum ConnectionDirection
      {
         Source,
         Target
      }

      public class DataverseEntity
      {
         public string Name { get; }
         public bool IsSystemEntity { get; }
         public bool IsActivityEntity { get; }

         public DataverseEntity(
            string name,
            bool isSystemEntity,
            bool isActivityEntity
         )
         {
            Name = name;
            IsSystemEntity = isSystemEntity;
            IsActivityEntity = isActivityEntity;
         }
      }

      /// <summary>
      /// Logical names for common Dataverse entities.
      /// </summary>
      public static class DataverseEntities
      {
         public static readonly DataverseEntity SystemUser =
            new DataverseEntity("systemuser", true, false);

         public static readonly DataverseEntity ActivityParty =
            new DataverseEntity("activityparty", true, false);

         public static readonly DataverseEntity Account =
            new DataverseEntity("account", false, false);

         public static readonly DataverseEntity Contact =
            new DataverseEntity("contact", false, false);

         public static readonly DataverseEntity Task =
            new DataverseEntity("task", false, true);

         public static readonly DataverseEntity PhoneCall =
            new DataverseEntity("phonecall", false, true);

         public static readonly DataverseEntity Appointment =
            new DataverseEntity("appointment", false, true);

         public static readonly DataverseEntity Email =
            new DataverseEntity("email", false, true);

         public static readonly DataverseEntity Lead =
            new DataverseEntity("lead", true, false);

         public static readonly DataverseEntity Opportunity =
            new DataverseEntity("opportunity", true, false);

         public static readonly DataverseEntity Quote =
            new DataverseEntity("quote", true, false);

         public static readonly DataverseEntity Order =
            new DataverseEntity("salesorder", true, false);

         public static readonly DataverseEntity Invoice =
            new DataverseEntity("invoice", true, false);

         public static readonly DataverseEntity Product =
            new DataverseEntity("product", true, false);

         public static readonly DataverseEntity PriceLevel =
            new DataverseEntity("pricelevel", true, false);

         public static readonly DataverseEntity Category =
            new DataverseEntity("category", true, false);

         public static readonly DataverseEntity Case =
            new DataverseEntity("incident", true, false);

         public static readonly DataverseEntity Entitlement =
            new DataverseEntity("entitlement", true, false);

         public static readonly DataverseEntity Solution =
            new DataverseEntity("solution", true, false);

         public static readonly DataverseEntity KnowledgeArticle =
            new DataverseEntity("knowledgearticle", true, false);

         public static readonly DataverseEntity Campaign =
            new DataverseEntity("campaign", true, false);

         public static readonly DataverseEntity CampaignActivity =
            new DataverseEntity("campaignactivity", true, true);

         public static readonly DataverseEntity MarketingList =
            new DataverseEntity("list", true, false);

         public static readonly DataverseEntity EmailTemplate =
            new DataverseEntity("template", true, false);

         public static readonly DataverseEntity Team =
            new DataverseEntity("team", true, false);

         public static readonly DataverseEntity Role =
            new DataverseEntity("role", true, false);

         public static readonly DataverseEntity BusinessUnit =
            new DataverseEntity("businessunit", true, false);


         /// <summary>
         /// Returns all entities defined in this class as a list, 
         /// sorted by system/activity status and then by name.
         /// </summary>
         public static List<DataverseEntity> ToList()
         {
            return typeof(DataverseEntities)
               .GetFields(
                  BindingFlags.Public |
                  BindingFlags.Static |
                  BindingFlags.FlattenHierarchy
               )
               .Where(f => f.FieldType == typeof(DataverseEntity))
               .Select(f => (DataverseEntity?)f.GetValue(null))
               .Where(e => e != null)
               .Select(e => e!)
               .OrderBy(e => e.IsSystemEntity)
               .ThenBy(e => e.IsActivityEntity)
               .ThenBy(e => e.Name)
               .ToList();
         }
      }

      /// <summary>
      /// Constants for the source data preservation entity.
      /// </summary>
      public static class SourceData
      {
         public const string EntityLogicalName = "dm_sourcedata";
         public const string PrimaryId = "dm_sourcedataid";
         public const string Name = "dm_name";
         public const string EntityId = "dm_sourceentityid";
         public const string EntityLogicalNameAttr =
            "dm_sourceentitylogicalname";
         public const string CreatedOn = "dm_sourcecreateddate";
         public const string ModifiedOn = "dm_sourcemodifieddate";
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
         public const string NotAvailable = "N/A";
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
         public const string SystemUserId = "systemuserid";
         public const string FullName = "fullname";
         public const string OwnerId = "ownerid";
         public const string CreatedBy = "createdby";
         public const string ModifiedBy = "modifiedby";
         public const string CreatedOnBehalfBy = "createdonbehalfby";
         public const string ModifiedOnBehalfBy = "modifiedonbehalfby";
         public const string OverriddenCreatedOn = "overriddencreatedon";
         public const string ImportSequenceNumber = "importsequencenumber";
         public const string VersionNumber = "versionnumber";
         public const string TransactionCurrencyId = "transactioncurrencyid";
         public const string ExchangeRate = "exchangerate";
         public const string ActivityPartyId = "activitypartyid";
         public const string ActivityId = "activityid";
         public const string PartyId = "partyid";
         public const string ParticipationTypeMask = "participationtypemask";
         public const string AddressUsed = "addressused";
         public const string Address1Id = "address1_addressid";
         public const string Address2Id = "address2_addressid";
         public const string Name = "name";
         public const string Telephone1 = "telephone1";
         public const string Address1Line1 = "address1_line1";
         public const string Address1City = "address1_city";
         public const string Address1PostalCode = "address1_postalcode";
         public const string WebsiteUrl = "websiteurl";
         public const string IsDisabled = "isdisabled";
         public const string AccessMode = "accessmode";
         public const string FirstName = "firstname";
         public const string LastName = "lastname";
         public const string EmailAddress1 = "emailaddress1";
         public const string JobTitle = "jobtitle";
         public const string Subject = "subject";
         public const string Description = "description";
         public const string ScheduledEnd = "scheduledend";
         public const string PhoneNumber = "phonenumber";
         public const string RegardingObjectId = "regardingobjectid";
         public const string To = "to";
         public const string ParentCustomerId = "parentcustomerid";
         public const string PrimaryContactId = "primarycontactid";
      }

      /// <summary>
      /// Attributes that should not be copied from source to target.
      /// </summary>
      public static readonly HashSet<string> ForbiddenAttributes =
         new HashSet<string>(StringComparer.OrdinalIgnoreCase)
         {
            DataverseAttributes.CreatedOn,
            DataverseAttributes.ModifiedOn,
            DataverseAttributes.CreatedBy,
            DataverseAttributes.ModifiedBy,
            DataverseAttributes.CreatedOnBehalfBy,
            DataverseAttributes.ModifiedOnBehalfBy,
            DataverseAttributes.OverriddenCreatedOn,
            DataverseAttributes.ImportSequenceNumber,
            DataverseAttributes.VersionNumber,
            DataverseAttributes.Address1Id,
            DataverseAttributes.Address2Id
         };

      /// <summary>
      /// Attributes that represent references to SystemUser records.
      /// </summary>
      public static readonly HashSet<string> UserAttributes =
         new HashSet<string>(StringComparer.OrdinalIgnoreCase)
         {
            DataverseAttributes.OwnerId,
            DataverseAttributes.CreatedBy,
            DataverseAttributes.ModifiedBy,
            DataverseAttributes.CreatedOnBehalfBy,
            DataverseAttributes.ModifiedOnBehalfBy
         };

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
         public const string ForeignKeyConflict =
            "conflicted with the foreign key constraint";
         public const string ConstraintConflict =
            "conflicted with a constraint";
         public const string CannotBeModified = "cannot be modified";
         public const string CannotBeSetOnCreation =
            "cannot be set on creation";
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
      /// Settings related to data synchronization.
      /// </summary>
      public static class SyncSettings
      {
         public static readonly int[] ParallelismOptions =
            new[]
            {
               10,
               20,
               30,
               40,
               50,
               1
            };

         public static IReadOnlyList<string> RecommendedEntities =>
            DataverseEntities.ToList()
               .Where(e => !e.IsSystemEntity)
               .Select(e => e.Name)
               .ToList()
               .AsReadOnly();
      }

      /// <summary>
      /// Spectre.Console markup and UI keywords.
      /// </summary>
      public static class UiMarkup
      {
         public const string Yellow = "[yellow]";
         public const string Red = "[red]";
         public const string Grey = "[grey]";
         public const string Green = "[green]";
         public const string Blue = "[blue]";
         public const string Cyan = "[cyan]";
         public const string BoldYellow = "[bold yellow]";
         public const string BoldRed = "[bold red]";
         public const string BoldGreen = "[bold green]";
         public const string BoldBlue = "[bold blue]";
         public const string BoldCyan = "[bold cyan]";
         public const string BoldMagenta = "[bold magenta]";
         public const string Wait = "WAIT";
         public const string WipeDataConfirmation = "WIPE DATA";
      }

      /// <summary>
      /// Constants for Dataverse plugin registration entities and attributes.
      /// </summary>
      public static class PluginRegistration
      {
         public const string AssemblyEntity = "pluginassembly";
         public const string AssemblyId = "pluginassemblyid";
         public const string AssemblyName = "name";
         public const string Content = "content";
         public const string IsolationMode = "isolationmode";
         public const string SourceType = "sourcetype";
         public const string PublicKeyToken = "publickeytoken";
         public const string Version = "version";
         public const string Culture = "culture";

         public const string TypeEntity = "plugintype";
         public const string TypeId = "plugintypeid";
         public const string TypeName = "typename";
         public const string FriendlyName = "friendlyname";

         public const string StepEntity = "sdkmessageprocessingstep";
         public const string StepId = "sdkmessageprocessingstepid";
         public const string Configuration = "configuration";
         public const string InvocationSource = "invocationsource";
         public const string Stage = "stage";
         public const string SupportedDeployment = "supporteddeployment";
         public const string Rank = "rank";
         public const string Mode = "mode";

         public const string EventHandler = "eventhandler";

         public const string MessageEntity = "sdkmessage";
         public const string MessageId = "sdkmessageid";
         public const string MessageName = "name";
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
         public const string AppName = "dvmig";
         public const string SettingsFileName = "settings.json";
         public const string StateFolderName = "state";
         public const string PluginAssemblyName = "dvmig.Plugins.dll";
         public const string PluginName = "dvmig.Plugins";
      }
   }
}
