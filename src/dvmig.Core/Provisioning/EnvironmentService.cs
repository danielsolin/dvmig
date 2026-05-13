using System.ServiceModel;

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

using dvmig.Core.Interfaces;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="IEnvironmentService"/> that manages 
   /// the environment lifecycle and readiness.
   /// </summary>
   /// <remarks>
   /// Initializes a new instance of the <see cref="EnvironmentService"/> 
   /// class.
   /// </remarks>
   /// <param name="logger">The logger instance.</param>
   public class EnvironmentService(ILogger logger)
      : Interfaces.IEnvironmentService
   {
      private readonly ILogger _logger = logger;

      private enum AttributeType
      {
         String,
         Memo,
         DateTime,
         Lookup
      }

      /// <inheritdoc />
      public async Task<bool> ValidateTargetEnvironmentAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         try
         {
            // 1. Check Failure Log Entity
            var failureMeta = await target.GetEntityMetadataAsync(
               MigrationFailure.EntityLogicalName,
               ct
            );

            if (failureMeta == null)
               return false;

            // 2. Check Source Data Entity
            var sourceDataMeta = await target.GetEntityMetadataAsync(
               SourceData.EntityLogicalName,
               ct
            );

            if (sourceDataMeta == null)
               return false;

            // 3. Check Plugin Assembly
            var assemblyQuery = new QueryByAttribute(
               PluginRegistration.AssemblyEntity
            )
            {
               ColumnSet = new ColumnSet(
                  PluginRegistration.AssemblyId
               )
            };

            assemblyQuery.AddAttributeValue(
               PluginRegistration.AssemblyName,
               AppConstants.PluginName
            );

            var assemblies = await target.RetrieveMultipleAsync(
               assemblyQuery,
               ct
            );

            if (assemblies?.Entities.Any() != true)
               return false;

            // 4. Check Plugin Type
            var typeQuery = new QueryByAttribute(
               PluginRegistration.TypeEntity
            )
            {
               ColumnSet = new ColumnSet(
                  PluginRegistration.TypeId
               )
            };

            typeQuery.AddAttributeValue(
               PluginRegistration.TypeName,
               $"{AppConstants.PluginName}.DMPlugin"
            );

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);

            if (types?.Entities.Any() != true)
               return false;

            var typeId = types.Entities.First().Id;

            // 5. Check Plugin Steps (Create & Update)
            var stepQuery = new QueryByAttribute(
               PluginRegistration.StepEntity
            )
            {
               ColumnSet = new ColumnSet(
                  PluginRegistration.MessageName
               )
            };

            stepQuery.AddAttributeValue(
               PluginRegistration.EventHandler,
               typeId
            );

            var steps = await target.RetrieveMultipleAsync(stepQuery, ct);

            bool hasCreate = steps?.Entities.Any(e =>
               e.GetAttributeValue<string>(
                  PluginRegistration.MessageName
               )?.Contains("Create") == true
            ) == true;

            bool hasUpdate = steps?.Entities.Any(e =>
               e.GetAttributeValue<string>(
                  PluginRegistration.MessageName
               )?.Contains("Update") == true
            ) == true;

            return hasCreate && hasUpdate;
         }
         catch
         {
            return false;
         }
      }

      /// <inheritdoc />
      public async Task InstallComponentsAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         _logger.Information("Starting component installation...");

         // 1. Create Schema
         await EnsureSourceDataEntityAsync(target, ct);
         await EnsureFailureLogEntityAsync(target, ct);

         _logger.Information("Publishing schema changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         // 2. Deploy Plugin
         await DeployPluginAssemblyAsync(target, null, ct);

         _logger.Information("Component installation completed.");
      }

      /// <inheritdoc />
      public async Task UninstallComponentsAsync(
         IDataverseProvider target,
         CancellationToken ct = default
      )
      {
         _logger.Information("Starting component uninstallation...");

         // 1. Remove Plugin
         await RemovePluginAssemblyAsync(target, ct);

         // 2. Publish after plugin removal
         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         // 3. Drop Schema
         await DropEntityIfPresentAsync(
            target,
            MigrationFailure.EntityLogicalName,
            ct
         );

         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         await DropEntityIfPresentAsync(
            target,
            SourceData.EntityLogicalName,
            ct
         );

         _logger.Information("Publishing changes...");
         await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

         _logger.Information("Component uninstallation completed.");
      }

      #region Schema Logic

      private async Task EnsureSourceDataEntityAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         var entityName = SourceData.EntityLogicalName;
         var existingMeta = await target.GetEntityMetadataAsync(
            entityName,
            ct
         );

         if (existingMeta == null)
         {
            _logger.Information(
               "Creating '{Entity}' entity...",
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
                     AppConstants.LanguageCode
                  ),
                  DisplayCollectionName = new Label(
                     "DVMig Source Data",
                     AppConstants.LanguageCode
                  ),
                  OwnershipType = OwnershipTypes.UserOwned,
                  IsActivity = false,
                  HasNotes = false,
                  HasActivities = false
               },
               PrimaryAttribute = new StringAttributeMetadata
               {
                  SchemaName = SourceData.Name,
                  LogicalName = SourceData.Name,
                  DisplayName = new Label("Name", AppConstants.LanguageCode),
                  RequiredLevel =
                     new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.None
                     ),
                  MaxLength = 100
               }
            };

            await target.ExecuteAsync(entityReq, ct);
            await Task.Delay(
               100,
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
            SourceData.EntityId,
            "Source Entity ID",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SourceData.EntityLogicalNameAttr,
            "Source Entity Logical Name",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SourceData.CreatedOn,
            "Source Created Date",
            ct,
            AttributeType.DateTime
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            SourceData.ModifiedOn,
            "Source Modified Date",
            ct,
            AttributeType.DateTime
         );
      }

      private async Task EnsureFailureLogEntityAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         var entityName = MigrationFailure.EntityLogicalName;
         var existingMeta = await target.GetEntityMetadataAsync(
            entityName,
            ct
         );

         if (existingMeta == null)
         {
            _logger.Information(
               "Creating '{Entity}' entity...",
               entityName
            );

            var entityReq = new CreateEntityRequest
            {
               Entity = new EntityMetadata
               {
                  SchemaName = entityName,
                  LogicalName = entityName,
                  DisplayName = new Label(
                     "DVMig Failure",
                     AppConstants.LanguageCode
                  ),
                  DisplayCollectionName = new Label(
                     "DVMig Failures",
                     AppConstants.LanguageCode
                  ),
                  OwnershipType = OwnershipTypes.UserOwned,
                  IsActivity = false
               },
               PrimaryAttribute = new StringAttributeMetadata
               {
                  SchemaName = MigrationFailure.Name,
                  LogicalName = MigrationFailure.Name,
                  DisplayName = new Label("Name", AppConstants.LanguageCode),
                  MaxLength = 100
               }
            };

            await target.ExecuteAsync(entityReq, ct);
            await Task.Delay(
               100,
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
            MigrationFailure.SourceId,
            "Source Record ID",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            MigrationFailure.EntityLogicalNameAttr,
            "Entity Logical Name",
            ct
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            MigrationFailure.ErrorMessage,
            "Error Message",
            ct,
            AttributeType.Memo
         );

         await CreateAttributeIfMissingAsync(
            target,
            entityName,
            existingMeta!,
            MigrationFailure.Timestamp,
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
            return;

         _logger.Information(
            "Creating attribute {Attr} on {Entity}...",
            schemaName,
            entityLogicalName
         );

         AttributeMetadata attr = type switch
         {
            AttributeType.Memo => new MemoAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, AppConstants.LanguageCode),
               MaxLength = AppConstants
                                .MaxMemoFieldLength
            },
            AttributeType.DateTime => new DateTimeAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, AppConstants.LanguageCode),
               Format = DateTimeFormat.DateAndTime
            },
            AttributeType.Lookup => new LookupAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, AppConstants.LanguageCode),
               Targets = new[] { lookupTarget! }
            },
            _ => new StringAttributeMetadata
            {
               SchemaName = schemaName,
               LogicalName = schemaName.ToLower(),
               DisplayName = new Label(displayName, AppConstants.LanguageCode),
               MaxLength = 200
            }
         };

         var req = new CreateAttributeRequest
         {
            EntityName = entityLogicalName,
            Attribute = attr
         };

         await target.ExecuteAsync(req, ct);
         await Task.Delay(10, ct); // Gap for consistency
      }

      private async Task DropEntityIfPresentAsync(
         IDataverseProvider target,
         string logicalName,
         CancellationToken ct
      )
      {
         _logger.Information(
            "Checking for '{Entity}' entity...",
            logicalName
         );

         var existingMeta = await target.GetEntityMetadataAsync(
            logicalName,
            ct
         );

         if (existingMeta != null)
         {
            _logger.Information(
               "Deleting '{Entity}' entity...",
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
               _logger.Warning(
                  "Deletion of {Entity} failed due to dependencies.",
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
                        blockers.Add(
                           $"Unknown Component {depId} (Type {depType})"
                        );
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
            _logger.Information(
               "'{Entity}' entity not found.",
               logicalName
            );
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
               62 => PluginRegistration.StepEntity,
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
               new[] { DataverseAttributes.Name },
               ct
            );

            return result?.GetAttributeValue<string>(
               DataverseAttributes.Name
            );
         }
         catch
         {
            return null;
         }
      }

      #endregion

      #region Plugin Logic

      private async Task DeployPluginAssemblyAsync(
         IDataverseProvider target,
         string? pluginAssemblyPath,
         CancellationToken ct
      )
      {
         var assemblyPath = pluginAssemblyPath;

         if (string.IsNullOrEmpty(assemblyPath))
         {
            assemblyPath = Path.Combine(
               AppDomain.CurrentDomain.BaseDirectory,
               AppConstants.PluginAssemblyName
            );

            // Fallback for development if not in same folder
            if (!File.Exists(assemblyPath))
               assemblyPath = Path.Combine(
                  AppDomain.CurrentDomain.BaseDirectory,
                  "..",
                  "..",
                  "..",
                  "..",
                  AppConstants.PluginName,
                  "bin",
                  "Debug",
                  "netstandard2.0",
                  AppConstants.PluginAssemblyName
               );
         }

         if (!File.Exists(assemblyPath))
         {
            var msg = $"Plugin assembly not found at {assemblyPath}. " +
               "Cannot proceed with installation.";

            _logger.Error(msg);

            throw new FileNotFoundException(msg, assemblyPath);
         }

         _logger.Information("Deploying plugin assembly...");

         var assemblyBytes = await Task.Run(
            () => File.ReadAllBytes(assemblyPath),
            ct
         );

         var assembly = new Entity(
            PluginRegistration.AssemblyEntity
         );

         assembly[PluginRegistration.AssemblyName] =
            AppConstants.PluginName;
         assembly[PluginRegistration.Content] =
            Convert.ToBase64String(assemblyBytes);
         assembly[PluginRegistration.IsolationMode] =
            new OptionSetValue(2); // Sandbox
         assembly[PluginRegistration.SourceType] =
            new OptionSetValue(0); // Database
         assembly[PluginRegistration.PublicKeyToken] =
            "397f674bbcd3d607";
         assembly[PluginRegistration.Version] = "1.0.0.0";
         assembly[PluginRegistration.Culture] = "neutral";

         var query = new QueryByAttribute(
            PluginRegistration.AssemblyEntity
         )
         {
            ColumnSet = new ColumnSet(
               PluginRegistration.AssemblyId
            )
         };

         query.AddAttributeValue(
            PluginRegistration.AssemblyName,
            AppConstants.PluginName
         );

         var existing = await target.RetrieveMultipleAsync(query, ct);
         Guid assemblyId;

         if (existing.Entities.Any())
         {
            assemblyId = existing.Entities.First().Id;
            assembly.Id = assemblyId;

            await target.UpdateAsync(assembly, ct);

            _logger.Information("Updated existing plugin assembly.");
         }
         else
         {
            assemblyId = await target.CreateAsync(assembly, ct);

            _logger.Information("Created new plugin assembly.");
         }

         await RegisterPluginStepAsync(target, assemblyId, ct);
      }

      private async Task RegisterPluginStepAsync(
         IDataverseProvider target,
         Guid assemblyId,
         CancellationToken ct
      )
      {
         _logger.Information("Registering plugin type and step...");

         var pluginTypeName =
            $"{AppConstants.PluginName}.DMPlugin";

         var typeQuery = new QueryByAttribute(
            PluginRegistration.TypeEntity
         )
         {
            ColumnSet = new ColumnSet(
               PluginRegistration.TypeId
            )
         };

         typeQuery.AddAttributeValue(
            PluginRegistration.AssemblyId,
            assemblyId
         );
         typeQuery.AddAttributeValue(
            PluginRegistration.TypeName,
            pluginTypeName
         );

         var types = await target.RetrieveMultipleAsync(typeQuery, ct);
         Guid typeId;

         if (types.Entities.Any())
         {
            typeId = types.Entities.First().Id;

            _logger.Information("Plugin type already registered.");
         }
         else
         {
            var type = new Entity(
               PluginRegistration.TypeEntity
            );

            type[PluginRegistration.AssemblyId] =
               new EntityReference(
                  PluginRegistration.AssemblyEntity,
                  assemblyId
               );
            type[PluginRegistration.TypeName] = pluginTypeName;
            type[PluginRegistration.AssemblyName] =
               pluginTypeName;
            type[PluginRegistration.FriendlyName] = "DMPlugin";

            typeId = await target.CreateAsync(type, ct);

            _logger.Information("Registered plugin type.");
         }

         await RegisterStepForMessageAsync(
            target,
            typeId,
            "Create",
            ct
         );

         await RegisterStepForMessageAsync(
            target,
            typeId,
            "Update",
            ct
         );
      }

      private async Task RegisterStepForMessageAsync(
         IDataverseProvider target,
         Guid typeId,
         string messageName,
         CancellationToken ct
      )
      {
         var msgQuery = new QueryByAttribute(
            PluginRegistration.MessageEntity
         )
         {
            ColumnSet = new ColumnSet(
               PluginRegistration.MessageId
            )
         };

         msgQuery.AddAttributeValue(
            PluginRegistration.MessageName,
            messageName
         );

         var msgs = await target.RetrieveMultipleAsync(msgQuery, ct);

         if (!msgs.Entities.Any())
            throw new Exception($"SdkMessage '{messageName}' not found.");

         var messageId = msgs.Entities.First().Id;

         var pluginTypeName =
            $"{AppConstants.PluginName}.DMPlugin";

         var step = new Entity(PluginRegistration.StepEntity);

         step[PluginRegistration.MessageName] =
            $"{pluginTypeName}: {messageName}";
         step[PluginRegistration.Configuration] = "";
         step[PluginRegistration.InvocationSource] =
            new OptionSetValue(0); // Internal
         step[PluginRegistration.MessageId] =
            new EntityReference(
               PluginRegistration.MessageEntity,
               messageId
            );
         step[PluginRegistration.TypeId] =
            new EntityReference(
               PluginRegistration.TypeEntity,
               typeId
            );
         step[PluginRegistration.Stage] =
            new OptionSetValue(20); // Pre-Operation
         step[PluginRegistration.SupportedDeployment] =
            new OptionSetValue(0); // Server
         step[PluginRegistration.Rank] = 1;
         step[PluginRegistration.Mode] =
            new OptionSetValue(0); // Synchronous
         step[PluginRegistration.EventHandler] =
            new EntityReference(
               PluginRegistration.TypeEntity,
               typeId
            );

         var stepQuery = new QueryByAttribute(
            PluginRegistration.StepEntity
         )
         {
            ColumnSet = new ColumnSet(
               PluginRegistration.StepId
            )
         };

         stepQuery.AddAttributeValue(
            PluginRegistration.EventHandler,
            typeId
         );
         stepQuery.AddAttributeValue(
            PluginRegistration.MessageId,
            messageId
         );

         var existingSteps = await target.RetrieveMultipleAsync(
            stepQuery,
            ct
         );

         if (existingSteps.Entities.Any())
         {
            step.Id = existingSteps.Entities.First().Id;

            await target.UpdateAsync(step, ct);

            _logger.Information(
               "Updated existing plugin step for {0}.",
               messageName
            );
         }
         else
         {
            await target.CreateAsync(step, ct);

            _logger.Information(
               "Created new plugin step for {0}.",
               messageName
            );
         }
      }

      private async Task RemovePluginAssemblyAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         _logger.Information("Searching for plugin assembly to remove...");

         var query = new QueryByAttribute(
            PluginRegistration.AssemblyEntity
         )
         {
            ColumnSet = new ColumnSet(
               PluginRegistration.AssemblyId
            )
         };

         query.AddAttributeValue(
            PluginRegistration.AssemblyName,
            AppConstants.PluginName
         );

         var result = await target.RetrieveMultipleAsync(query, ct);

         if (result.Entities.Any())
         {
            var assemblyId = result.Entities.First().Id;

            _logger.Information(
               "Found plugin assembly. " +
               "Identifying dependent components..."
            );

            var typeQuery = new QueryByAttribute(
               PluginRegistration.TypeEntity
            )
            {
               ColumnSet = new ColumnSet(
                  PluginRegistration.TypeId,
                  PluginRegistration.TypeName
               )
            };

            typeQuery.AddAttributeValue(
               PluginRegistration.AssemblyId,
               assemblyId
            );

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);

            foreach (var type in types.Entities)
            {
               var typeName = type.GetAttributeValue<string>(
                  PluginRegistration.TypeName
               );

               var stepQuery = new QueryByAttribute(
                  PluginRegistration.StepEntity
               )
               {
                  ColumnSet = new ColumnSet(
                     PluginRegistration.StepId,
                     PluginRegistration.MessageName
                  )
               };

               stepQuery.AddAttributeValue(
                  PluginRegistration.EventHandler,
                  type.Id
               );

               var steps = await target.RetrieveMultipleAsync(
                  stepQuery,
                  ct
               );

               foreach (var step in steps.Entities)
               {
                  var stepName = step.GetAttributeValue<string>(
                     PluginRegistration.MessageName
                  );

                  _logger.Information(
                     $"Deleting plugin step: {stepName}..."
                  );

                  await target.DeleteAsync(
                     PluginRegistration.StepEntity,
                     step.Id,
                     ct
                  );
               }

               _logger.Information($"Deleting plugin type: {typeName}...");

               await target.DeleteAsync(
                  PluginRegistration.TypeEntity,
                  type.Id,
                  ct
               );
            }

            _logger.Information("Deleting plugin assembly...");

            await target.DeleteAsync(
               PluginRegistration.AssemblyEntity,
               assemblyId,
               ct
            );

            _logger.Information("Plugin assembly removed successfully.");
         }
         else
            _logger.Information("No plugin assembly found to remove.");
      }

      #endregion
   }
}
