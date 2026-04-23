using dvmig.Providers;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core
{
    public class SetupService : ISetupService
    {
        private readonly ILogger _logger;

        public SetupService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsEnvironmentReadyAsync(
            IDataverseProvider target,
            CancellationToken ct = default)
        {
            try
            {
                if (!await IsEntitySchemaReadyAsync(target, ct)) return false;

                var assemblyId = await GetPluginAssemblyIdAsync(target, ct);
                if (assemblyId == null) return false;

                var pluginTypeId = await GetPluginTypeIdAsync(target, assemblyId.Value, ct);
                if (pluginTypeId == null) return false;

                return await HasRequiredPluginStepsAsync(target, pluginTypeId.Value, ct);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsEntitySchemaReadyAsync(
            IDataverseProvider target,
            CancellationToken ct)
        {
            var meta = await target.GetEntityMetadataAsync("dm_sourcedate", ct);
            return meta != null;
        }

        private async Task<Guid?> GetPluginAssemblyIdAsync(
            IDataverseProvider target,
            CancellationToken ct)
        {
            var query = new QueryByAttribute("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid")
            };
            query.AddAttributeValue("name", "dvmig.Plugins");

            var assemblies = await target.RetrieveMultipleAsync(query, ct);
            return assemblies.Entities.FirstOrDefault()?.Id;
        }

        private async Task<Guid?> GetPluginTypeIdAsync(
            IDataverseProvider target,
            Guid assemblyId,
            CancellationToken ct)
        {
            var typeQuery = new QueryByAttribute("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid")
            };
            typeQuery.AddAttributeValue("pluginassemblyid", assemblyId);
            typeQuery.AddAttributeValue("typename", "dvmig.Plugins.DMPlugin");

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);
            return types.Entities.FirstOrDefault()?.Id;
        }

        private async Task<bool> HasRequiredPluginStepsAsync(
            IDataverseProvider target,
            Guid pluginTypeId,
            CancellationToken ct)
        {
            var stepQuery = new QueryByAttribute("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("sdkmessageprocessingstepid")
            };
            stepQuery.AddAttributeValue("plugintypeid", pluginTypeId);

            var steps = await target.RetrieveMultipleAsync(stepQuery, ct);

            // We require both Create and Update steps to be registered
            return steps.Entities.Count >= 2;
        }

        public async Task CreateSchemaAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var existingMeta = await target.GetEntityMetadataAsync(
                "dm_sourcedate",
                ct
            );

            if (existingMeta == null)
            {
                _logger.Information(
                    "Creating 'dm_sourcedate' entity schema..."
                );

                progress?.Report("Creating 'dm_sourcedate' entity schema...");

                var entityReq = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = "dm_sourcedate",
                        LogicalName = "dm_sourcedate",
                        DisplayName = new Label(
                            "Source Date Preservation",
                            1033
                        ),
                        DisplayCollectionName = new Label(
                            "Source Dates",
                            1033
                        ),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false,
                        HasNotes = false,
                        HasActivities = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = "dm_name",
                        LogicalName = "dm_name",
                        DisplayName = new Label("Name", 1033),
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.None
                        ),
                        MaxLength = 100
                    }
                };

                await target.ExecuteAsync(entityReq, ct);

                _logger.Information(
                    "Entity created. Waiting for metadata propagation..."
                );

                progress?.Report("Waiting for metadata propagation...");

                // Mandatory wait for Dataverse Online metadata propagation
                await Task.Delay(5000, ct);

                existingMeta = await target.GetEntityMetadataAsync(
                    "dm_sourcedate",
                    ct
                );
            }
            else
            {
                _logger.Information(
                    "'dm_sourcedate' entity already exists. Checking attributes."
                );

                progress?.Report(
                    "'dm_sourcedate' already exists. Checking attributes."
                );
            }

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourceentityid",
                "Source Entity ID",
                true,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourceentitylogicalname",
                "Source Entity Logical Name",
                true,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourcecreateddate",
                "Source Created Date",
                false,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            await CreateAttributeIfMissingAsync(
                target,
                existingMeta!,
                "dm_sourcemodifieddate",
                "Source Modified Date",
                false,
                progress,
                ct
            );
            await Task.Delay(2000, ct);

            _logger.Information("Publishing changes...");
            progress?.Report("Publishing changes...");

            await target.ExecuteAsync(new PublishAllXmlRequest(), ct);

            _logger.Information("Schema creation completed.");
            progress?.Report("Schema creation completed.");
        }

        private async Task CreateAttributeIfMissingAsync(
            IDataverseProvider target,
            EntityMetadata entityMeta,
            string schemaName,
            string displayName,
            bool isString,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            if (entityMeta.Attributes.Any(a => a.LogicalName == schemaName))
            {
                return;
            }

            _logger.Information("Creating attribute {Attr}...", schemaName);
            progress?.Report($"Creating attribute {schemaName}...");

            var req = new CreateAttributeRequest
            {
                EntityName = "dm_sourcedate",
                Attribute = isString
                    ? (AttributeMetadata)new StringAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, 1033),
                        MaxLength = 100
                    }
                    : new DateTimeAttributeMetadata
                    {
                        SchemaName = schemaName,
                        LogicalName = schemaName.ToLower(),
                        DisplayName = new Label(displayName, 1033),
                        Format = DateTimeFormat.DateAndTime
                    }
            };

            await target.ExecuteAsync(req, ct);
        }

        public async Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(pluginAssemblyPath))
            {
                throw new FileNotFoundException(
                    "Plugin assembly DLL not found.",
                    pluginAssemblyPath
                );
            }

            _logger.Information("Deploying plugin assembly...");
            progress?.Report("Deploying plugin assembly...");

            var assemblyBytes = await File.ReadAllBytesAsync(
                pluginAssemblyPath,
                ct
            );

            var assembly = new Entity("pluginassembly");
            assembly["name"] = "dvmig.Plugins";
            assembly["content"] = Convert.ToBase64String(assemblyBytes);
            assembly["isolationmode"] = new OptionSetValue(2); // Sandbox
            assembly["sourcetype"] = new OptionSetValue(0);    // Database
            assembly["publickeytoken"] = "397f674bbcd3d607";
            assembly["version"] = "1.0.0.0";
            assembly["culture"] = "neutral";

            // Check if exists for update vs create
            var query = new QueryByAttribute("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid")
            };
            query.AddAttributeValue("name", "dvmig.Plugins");

            var existing = await target.RetrieveMultipleAsync(query, ct);
            Guid assemblyId;

            if (existing.Entities.Any())
            {
                assemblyId = existing.Entities.First().Id;
                assembly.Id = assemblyId;

                await target.UpdateAsync(assembly, ct);

                _logger.Information("Updated existing plugin assembly.");
                progress?.Report("Updated existing plugin assembly.");
            }
            else
            {
                assemblyId = await target.CreateAsync(assembly, ct);

                _logger.Information("Created new plugin assembly.");
                progress?.Report("Created new plugin assembly.");
            }

            await RegisterPluginStepAsync(target, assemblyId, progress, ct);
        }

        private async Task RegisterPluginStepAsync(
            IDataverseProvider target,
            Guid assemblyId,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            _logger.Information("Registering plugin type and step...");
            progress?.Report("Registering plugin type and step...");

            // 1. Ensure Plugin Type exists
            var typeQuery = new QueryByAttribute("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid")
            };
            typeQuery.AddAttributeValue("pluginassemblyid", assemblyId);
            typeQuery.AddAttributeValue("typename", "dvmig.Plugins.DMPlugin");

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);
            Guid typeId;

            if (types.Entities.Any())
            {
                typeId = types.Entities.First().Id;

                _logger.Information("Plugin type already registered.");
                progress?.Report("Plugin type already registered.");
            }
            else
            {
                var type = new Entity("plugintype");
                type["pluginassemblyid"] = new EntityReference(
                    "pluginassembly",
                    assemblyId
                );
                type["typename"] = "dvmig.Plugins.DMPlugin";
                type["name"] = "dvmig.Plugins.DMPlugin";
                type["friendlyname"] = "DMPlugin";

                typeId = await target.CreateAsync(type, ct);

                _logger.Information("Registered plugin type.");
                progress?.Report("Registered plugin type.");
            }

            await RegisterStepForMessageAsync(
                target,
                typeId,
                "Create",
                progress,
                ct
            );

            await RegisterStepForMessageAsync(
                target,
                typeId,
                "Update",
                progress,
                ct
            );
        }

        private async Task RegisterStepForMessageAsync(
            IDataverseProvider target,
            Guid typeId,
            string messageName,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            // 1. Find Message ID
            var msgQuery = new QueryByAttribute("sdkmessage")
            {
                ColumnSet = new ColumnSet("sdkmessageid")
            };
            msgQuery.AddAttributeValue("name", messageName);

            var msgs = await target.RetrieveMultipleAsync(msgQuery, ct);

            if (!msgs.Entities.Any())
            {
                throw new Exception($"SdkMessage '{messageName}' not found.");
            }

            var messageId = msgs.Entities.First().Id;

            // 2. Define Step
            var step = new Entity("sdkmessageprocessingstep");
            step["name"] = $"dvmig.Plugins.DMPlugin: {messageName}";
            step["configuration"] = "";
            step["invocationsource"] = new OptionSetValue(0); // Internal
            step["sdkmessageid"] = new EntityReference(
                "sdkmessage",
                messageId
            );
            step["plugintypeid"] = new EntityReference(
                "plugintype",
                typeId
            );
            step["stage"] = new OptionSetValue(20);           // Pre-operation
            step["supporteddeployment"] = new OptionSetValue(0); // Server
            step["rank"] = 1;
            step["mode"] = new OptionSetValue(0);             // Synchronous

            // 3. Check if exists
            var stepQuery = new QueryByAttribute("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("sdkmessageprocessingstepid")
            };
            stepQuery.AddAttributeValue("plugintypeid", typeId);
            stepQuery.AddAttributeValue("sdkmessageid", messageId);

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

                progress?.Report(
                    $"Updated existing plugin step for {messageName}."
                );
            }
            else
            {
                await target.CreateAsync(step, ct);

                _logger.Information(
                    "Created new plugin step for {0}.",
                    messageName
                );

                progress?.Report(
                    $"Created new plugin step for {messageName}."
                );
            }
        }
    }
}
