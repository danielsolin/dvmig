using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System.ServiceModel;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Manages the creation of required schema components for migration.
   /// </summary>
   public class SchemaService : ISchemaService
   {
      private const int LanguageCode = 1033;
      private readonly ILogger _logger;

      private enum AttributeType
      {
         String,
         Memo,
         DateTime,
         Lookup
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="SchemaService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public SchemaService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task CreateSchemaAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         // 1. dm_sourcedata
         await EnsureSourceDataEntityAsync(target, ct);

         // 2. dm_migrationfailure
         await EnsureFailureLogEntityAsync(target, ct);

         _logger.Information("Publishing changes...");

         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         _logger.Information("Schema creation completed.");
      }

      private async Task EnsureSourceDataEntityAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         var entityName = SystemConstants.SourceData.EntityLogicalName;
         var existingMeta = await target.GetEntityMetadataAsync(
            entityName,
            ct
         );

         if (existingMeta == null)
         {
            _logger.Information("Creating '{Entity}' entity...",
               entityName
            );

            var entityReq = new CreateEntityRequest
            {
               Entity = new EntityMetadata
               {
                  SchemaName = entityName,
                  LogicalName = entityName,
                  DisplayName = new Label(
                     "DVMig Source Data", 
                     LanguageCode
                  ),
                  DisplayCollectionName = new Label(
                     "DVMig Source Data", 
                     LanguageCode
                  ),
                  OwnershipType = OwnershipTypes.UserOwned,
                  IsActivity = false,
                  HasNotes = false,
                  HasActivities = false
               },
               PrimaryAttribute = new StringAttributeMetadata
               {
                  SchemaName = SystemConstants.SourceData.Name,
                  LogicalName = SystemConstants.SourceData.Name,
                  DisplayName = new Label("Name", LanguageCode),
                  RequiredLevel =
                     new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.None
                     ),
                  MaxLength = 100
               }
            };

            await target.ExecuteAsync(entityReq, ct);
            await Task.Delay(
               SystemConstants.AppConstants.MetadataPropagationDelayMs,
               ct
            ); // Wait for propagation

            existingMeta = await target.GetEntityMetadataAsync(
               entityName,
               ct
            );
         }

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.EntityId,
            "Source Entity ID",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.EntityLogicalNameAttr,
            "Source Entity Logical Name",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.CreatedOn,
            "Source Created Date",
            ct,
            AttributeType.DateTime
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.ModifiedOn,
            "Source Modified Date",
            ct,
            AttributeType.DateTime
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.CreatedBy,
            "Source Created By",
            ct,
            AttributeType.String
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.SourceData.ModifiedBy,
            "Source Modified By",
            ct,
            AttributeType.String
         );
      }

      private async Task EnsureFailureLogEntityAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         var entityName = SystemConstants.MigrationFailure.EntityLogicalName;
         var existingMeta = await target.GetEntityMetadataAsync(
            entityName,
            ct
         );

         if (existingMeta == null)
         {
            _logger.Information("Creating '{Entity}' entity...",
               entityName
            );

            var entityReq = new CreateEntityRequest
            {
               Entity = new EntityMetadata
               {
                  SchemaName = entityName,
                  LogicalName = entityName,
                  DisplayName = new Label("DVMig Failure", LanguageCode),
                  DisplayCollectionName = new Label(
                     "DVMig Failures",
                     LanguageCode
                  ),
                  OwnershipType = OwnershipTypes.UserOwned,
                  IsActivity = false
               },
               PrimaryAttribute = new StringAttributeMetadata
               {
                  SchemaName = SystemConstants.MigrationFailure.Name,
                  LogicalName = SystemConstants.MigrationFailure.Name,
                  DisplayName = new Label("Name", LanguageCode),
                  MaxLength = 100
               }
            };

            await target.ExecuteAsync(entityReq, ct);
            await Task.Delay(
               SystemConstants.AppConstants.MetadataPropagationDelayMs,
               ct
            );

            existingMeta = await target.GetEntityMetadataAsync(
               entityName,
               ct
            );
         }

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.SourceId,
            "Source Record ID",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.EntityLogicalNameAttr,
            "Entity Logical Name",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.ErrorMessage,
            "Error Message",
            ct,
            AttributeType.Memo
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SystemConstants.MigrationFailure.Timestamp,
            "Failure Timestamp",
            ct,
            AttributeType.DateTime
         );
      }

      private async Task CreateAttributeIfMissingAsync(
         IDataverseProvider target,
         string entityLogicalName,
         EntityMetadata entityMeta,
         string schemaName,
         string displayName,
         CancellationToken ct,
         AttributeType type = AttributeType.String,
         string? lookupTarget = null
      )
      {
         if (entityMeta.Attributes != null &&
             entityMeta.Attributes.Any(a => a.LogicalName == schemaName))
         {
            return;
         }

         _logger.Information("Creating attribute {Attr} on {Entity}...",
            schemaName,
            entityLogicalName
         );

         AttributeMetadata attr = type switch
         {
            AttributeType.Memo => new MemoAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               MaxLength = SystemConstants.AppConstants
                                .MaxMemoFieldLength
            },
            AttributeType.DateTime => new DateTimeAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               Format = DateTimeFormat.DateAndTime
            },
            AttributeType.Lookup => new LookupAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               Targets = new[] { lookupTarget! }
            },
            _ => new StringAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, LanguageCode),
               MaxLength = 200
            }
         };

         var req = new CreateAttributeRequest
         {
            EntityName = entityLogicalName,
            Attribute = attr
         };

         await target.ExecuteAsync(req, ct);
         await Task.Delay(2000, ct); // Gap for consistency
      }

      /// <inheritdoc />
      public async Task DropSchemaAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         // 1. dm_migrationfailure (Delete this first as it might reference 
         //    dm_sourcedata if lookup was manually added)
         await DropEntityIfPresentAsync(
            target,
            SystemConstants.MigrationFailure.EntityLogicalName,
            ct
         );

         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         // 2. dm_sourcedata
         await DropEntityIfPresentAsync(
            target,
            SystemConstants.SourceData.EntityLogicalName,
            ct
         );

         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         _logger.Information("Schema removal completed.");
      }

      private async Task DropEntityIfPresentAsync(
         IDataverseProvider target,
         string logicalName,
         CancellationToken ct
      )
      {
         _logger.Information("Checking for '{Entity}' entity...",
            logicalName
         );

         var existingMeta = await target.GetEntityMetadataAsync(
            logicalName,
            ct
         );

         if (existingMeta != null)
         {
            _logger.Information("Deleting '{Entity}' entity...",
               logicalName
            );

            try
            {
               var request = new DeleteEntityRequest
               {
                  LogicalName = logicalName
               };

               await target.ExecuteAsync(request, ct);
            }
            catch (FaultException ex) when (
               ex.Message.Contains("referenced by")
            )
            {
               _logger.Warning("Deletion of {Entity} failed due to dependencies.",
                  logicalName
               );

               var depReq = new RetrieveDependenciesForDeleteRequest
               {
                  ComponentType = 1, // Entity
                  ObjectId = existingMeta.MetadataId ?? Guid.Empty
               };

               var depRes = await target.ExecuteAsync(depReq, ct) 
                  as RetrieveDependenciesForDeleteResponse;

               var blockers = new List<string>();

               if (depRes?.EntityCollection.Entities.Any() == true)
               {
                  foreach (var dep in depRes.EntityCollection.Entities)
                  {
                     var depType = dep.GetAttributeValue<OptionSetValue>(
                        "dependentcomponenttype")?.Value;
                     var depId = dep.GetAttributeValue<Guid>(
                        "dependentcomponentobjectid");

                     string? depName = await TryGetDependencyNameAsync(
                        target, 
                        depType ?? 0, 
                        depId, 
                        ct
                     );

                     if (!string.IsNullOrEmpty(depName))
                        blockers.Add($"{depName} (Type {depType})");
                     else
                     {
                        blockers.Add(
                           $"Unknown Component {depId} (Type {depType})"
                        );
                     }
                  }
               }

               var blockerList = blockers.Count > 0 
                  ? string.Join(", ", blockers) 
                  : "unidentified components";

               var errorMsg = 
                  $"Cannot delete entity '{logicalName}' because it is " +
                  $"referenced by: {blockerList}. Please manually remove " +
                  "these references (e.g., from Model-driven Apps, " +
                  "Sitemaps, or Solutions) before trying again.";

               _logger.Error(errorMsg);

               throw new InvalidOperationException(errorMsg, ex);
            }
         }
         else
         {
            _logger.Information("'{Entity}' entity not found.",
               logicalName
            );
         }
      }

      private async Task<string?> TryGetDependencyNameAsync(
         IDataverseProvider target,
         int type,
         Guid id,
         CancellationToken ct
      )
      {
         try
         {
            string? entityName = type switch
            {
               62 => SystemConstants.PluginRegistration.StepEntity,
               80 => "appmodule",
               29 => "workflow",
               60 => "systemform",
               24 => "systemform",
               _ => null
            };

            if (entityName == null)
               return null;

            var result = await target.RetrieveAsync(
               entityName,
               id,
               new[] { SystemConstants.DataverseAttributes.Name },
               ct
            );

            return result?.GetAttributeValue<string>(
               SystemConstants.DataverseAttributes.Name
            );
         }
         catch
         {
            return null;
         }
      }
   }
}
