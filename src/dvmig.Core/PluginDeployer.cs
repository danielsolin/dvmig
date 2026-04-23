using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core
{
    /// <summary>
    /// Defines the contract for deploying and registering plugins in the
    /// target environment.
    /// </summary>
    public interface IPluginDeployer
    {
        /// <summary>
        /// Deploys the date preservation plugin assembly to the target 
        /// environment and registers the necessary execution steps.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="pluginAssemblyPath">The local path to the plugin DLL.
        /// </param>
        /// <param name="progress">An optional progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task representing the asynchronous deployment.</returns>
        Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );
    }

    /// <summary>
    /// Handles the deployment and registration of Dataverse plugins.
    /// </summary>
    public class PluginDeployer : IPluginDeployer
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginDeployer"/>
        /// class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public PluginDeployer(ILogger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
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
            IProgress<string>? progress,
            CancellationToken ct
        )
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
            IProgress<string>? progress,
            CancellationToken ct
        )
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
