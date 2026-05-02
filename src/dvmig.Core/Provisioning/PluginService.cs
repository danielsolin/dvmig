using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Handles the deployment and registration of Dataverse plugins.
   /// </summary>
   public class PluginService : IPluginService
   {
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the <see cref="PluginService"/>
      /// class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public PluginService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task DeployPluginAsync(
          IDataverseProvider target,
          string? pluginAssemblyPath = null,
          CancellationToken ct = default
      )
      {
         var assemblyPath = pluginAssemblyPath;

         if (string.IsNullOrEmpty(assemblyPath))
         {
            assemblyPath = Path.Combine(
               AppDomain.CurrentDomain.BaseDirectory,
               SystemConstants.AppConstants.PluginAssemblyName
            );

            // Fallback for development if not in same folder
            if (!File.Exists(assemblyPath))
               assemblyPath = Path.Combine(
                  AppDomain.CurrentDomain.BaseDirectory,
                  "..", "..", "..", "..",
                  SystemConstants.AppConstants.PluginName,
                  "bin", "Debug", "netstandard2.0",
                  SystemConstants.AppConstants.PluginAssemblyName
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

         var assemblyBytes = await File.ReadAllBytesAsync(
             assemblyPath,
             ct
         );

         var assembly = new Entity(
             SystemConstants.PluginRegistration.AssemblyEntity
         );
         assembly[SystemConstants.PluginRegistration.AssemblyName] =
             SystemConstants.AppConstants.PluginName;
         assembly[SystemConstants.PluginRegistration.Content] =
             Convert.ToBase64String(assemblyBytes);
         assembly[SystemConstants.PluginRegistration.IsolationMode] =
             new OptionSetValue(2); // Sandbox
         assembly[SystemConstants.PluginRegistration.SourceType] =
             new OptionSetValue(0);    // Database
         assembly[SystemConstants.PluginRegistration.PublicKeyToken] =
             "397f674bbcd3d607";
         assembly[SystemConstants.PluginRegistration.Version] = "1.0.0.0";
         assembly[SystemConstants.PluginRegistration.Culture] = "neutral";

         // Check if exists for update vs create
         var query = new QueryByAttribute(
             SystemConstants.PluginRegistration.AssemblyEntity
         )
         {
            ColumnSet = new ColumnSet(
                SystemConstants.PluginRegistration.AssemblyId
            )
         };
         query.AddAttributeValue(
             SystemConstants.PluginRegistration.AssemblyName,
             SystemConstants.AppConstants.PluginName
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

      /// <summary>
      /// Registers the plugin type and its corresponding execution steps 
      /// (Create and Update) for the newly deployed assembly.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="assemblyId">The ID of the deployed assembly.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      private async Task RegisterPluginStepAsync(
          IDataverseProvider target,
          Guid assemblyId,
          CancellationToken ct
      )
      {
         _logger.Information("Registering plugin type and step...");

         var pluginTypeName =
             $"{SystemConstants.AppConstants.PluginName}.DMPlugin";

         // 1. Ensure Plugin Type exists
         var typeQuery = new QueryByAttribute(
             SystemConstants.PluginRegistration.TypeEntity
         )
         {
            ColumnSet = new ColumnSet(
                SystemConstants.PluginRegistration.TypeId
            )
         };
         typeQuery.AddAttributeValue(
             SystemConstants.PluginRegistration.AssemblyId,
             assemblyId
         );
         typeQuery.AddAttributeValue(
             SystemConstants.PluginRegistration.TypeName,
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
                SystemConstants.PluginRegistration.TypeEntity
            );
            type[SystemConstants.PluginRegistration.AssemblyId] =
                new EntityReference(
                    SystemConstants.PluginRegistration.AssemblyEntity,
                    assemblyId
                );
            type[SystemConstants.PluginRegistration.TypeName] = pluginTypeName;
            type[SystemConstants.PluginRegistration.AssemblyName] =
                pluginTypeName;
            type[SystemConstants.PluginRegistration.FriendlyName] = "DMPlugin";

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

      /// <summary>
      /// Registers a specific SDK message processing step (e.g., Create, 
      /// Update) for the plugin type to execute synchronously in the 
      /// Pre-operation stage.
      /// </summary>
      /// <param name="target">The target Dataverse provider.</param>
      /// <param name="typeId">The ID of the plugin type.</param>
      /// <param name="messageName">The name of the SDK message.</param>
      /// <param name="progress">An optional progress reporter.</param>
      /// <param name="ct">A cancellation token.</param>
      private async Task RegisterStepForMessageAsync(
          IDataverseProvider target,
          Guid typeId,
          string messageName,
          CancellationToken ct
      )
      {
         // 1. Find Message ID
         var msgQuery = new QueryByAttribute(
             SystemConstants.PluginRegistration.MessageEntity
         )
         {
            ColumnSet = new ColumnSet(
                SystemConstants.PluginRegistration.MessageId
            )
         };
         msgQuery.AddAttributeValue(
             SystemConstants.PluginRegistration.MessageName,
             messageName
         );

         var msgs = await target.RetrieveMultipleAsync(msgQuery, ct);

         if (!msgs.Entities.Any())
            throw new Exception($"SdkMessage '{messageName}' not found.");

         var messageId = msgs.Entities.First().Id;

         var pluginTypeName =
             $"{SystemConstants.AppConstants.PluginName}.DMPlugin";

         // 2. Define Step
         var step = new Entity(SystemConstants.PluginRegistration.StepEntity);
         step[SystemConstants.PluginRegistration.MessageName] =
             $"{pluginTypeName}: {messageName}";
         step[SystemConstants.PluginRegistration.Configuration] = "";
         step[SystemConstants.PluginRegistration.InvocationSource] =
             new OptionSetValue(0); // Internal
         step[SystemConstants.PluginRegistration.MessageId] =
             new EntityReference(
                 SystemConstants.PluginRegistration.MessageEntity,
                 messageId
             );
         step[SystemConstants.PluginRegistration.TypeId] =
             new EntityReference(
                 SystemConstants.PluginRegistration.TypeEntity,
                 typeId
             );
         step[SystemConstants.PluginRegistration.Stage] =
             new OptionSetValue(20);           // Pre-operation
         step[SystemConstants.PluginRegistration.SupportedDeployment] =
             new OptionSetValue(0); // Server
         step[SystemConstants.PluginRegistration.Rank] = 1;
         step[SystemConstants.PluginRegistration.Mode] =
             new OptionSetValue(0);             // Synchronous
         step[SystemConstants.PluginRegistration.EventHandler] =
             new EntityReference(
                 SystemConstants.PluginRegistration.TypeEntity,
                 typeId
             );

         // 3. Check if exists
         var stepQuery = new QueryByAttribute(
             SystemConstants.PluginRegistration.StepEntity
         )
         {
            ColumnSet = new ColumnSet(
                SystemConstants.PluginRegistration.StepId
            )
         };
         stepQuery.AddAttributeValue(
             SystemConstants.PluginRegistration.EventHandler,
             typeId
         );
         stepQuery.AddAttributeValue(
             SystemConstants.PluginRegistration.MessageId,
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

            _logger.Information("Updated existing plugin step for {0}.",
                messageName
            );
         }
         else
         {
            await target.CreateAsync(step, ct);

            _logger.Information("Created new plugin step for {0}.",
                messageName
            );
         }
      }

      /// <inheritdoc />
      public async Task RemovePluginAsync(
          IDataverseProvider target,
          CancellationToken ct = default
      )
      {
         _logger.Information("Searching for plugin assembly to remove..."
         );

         var query = new QueryByAttribute(
             SystemConstants.PluginRegistration.AssemblyEntity
         )
         {
            ColumnSet = new ColumnSet(
                SystemConstants.PluginRegistration.AssemblyId
            )
         };
         query.AddAttributeValue(
             SystemConstants.PluginRegistration.AssemblyName,
             SystemConstants.AppConstants.PluginName
         );

         var result = await target.RetrieveMultipleAsync(query, ct);

         if (result.Entities.Any())
         {
            var assemblyId = result.Entities.First().Id;
            _logger.Information(
                "Found plugin assembly. " +
                "Identifying dependent components..."
            );

            // 1. Find all types in this assembly
            var typeQuery = new QueryByAttribute(
                SystemConstants.PluginRegistration.TypeEntity
            )
            {
               ColumnSet = new ColumnSet(
                   SystemConstants.PluginRegistration.TypeId,
                   SystemConstants.PluginRegistration.TypeName
               )
            };
            typeQuery.AddAttributeValue(
                SystemConstants.PluginRegistration.AssemblyId,
                assemblyId
            );
            var types = await target.RetrieveMultipleAsync(typeQuery, ct);

            foreach (var type in types.Entities)
            {
               var typeName = type.GetAttributeValue<string>(
                   SystemConstants.PluginRegistration.TypeName
               );

               // 2. Find and delete steps for each type
               var stepQuery = new QueryByAttribute(
                   SystemConstants.PluginRegistration.StepEntity
               )
               {
                  ColumnSet = new ColumnSet(
                       SystemConstants.PluginRegistration.StepId,
                       SystemConstants.PluginRegistration.MessageName
                   )
               };
               stepQuery.AddAttributeValue(
                   SystemConstants.PluginRegistration.EventHandler,
                   type.Id
               );
               var steps = await target.RetrieveMultipleAsync(
                   stepQuery,
                   ct
               );

               foreach (var step in steps.Entities)
               {
                  var stepName = step.GetAttributeValue<string>(
                      SystemConstants.PluginRegistration.MessageName
                  );
                  _logger.Debug(
                      "Deleting plugin step {Name} ({Id})",
                      stepName,
                      step.Id
                  );

                  _logger.Information(
                      $"Deleting plugin step: {stepName}..."
                  );

                  await target.DeleteAsync(
                      SystemConstants.PluginRegistration.StepEntity,
                      step.Id,
                      ct
                  );
               }

               _logger.Debug(
                   "Deleting plugin type {Name} ({Id})",
                   typeName,
                   type.Id
               );

               _logger.Information($"Deleting plugin type: {typeName}...");
               await target.DeleteAsync(
                   SystemConstants.PluginRegistration.TypeEntity,
                   type.Id,
                   ct
               );
            }

            _logger.Information(
                "Found plugin assembly {Id}. Deleting...",
                assemblyId
            );

            _logger.Information("Deleting plugin assembly...");

            await target.DeleteAsync(
                SystemConstants.PluginRegistration.AssemblyEntity,
                assemblyId,
                ct
            );

            _logger.Information("Plugin assembly removed successfully.");
         }
         else
         {
            _logger.Information("No plugin assembly found to remove.");
         }
      }
   }
}
