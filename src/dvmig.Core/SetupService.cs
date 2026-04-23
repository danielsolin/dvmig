using dvmig.Providers;
using Serilog;

namespace dvmig.Core
{
    /// <summary>
    /// Service responsible for preparing the target Dataverse environment for 
    /// migration, including schema creation and plugin deployment.
    /// </summary>
    public class SetupService : ISetupService
    {
        private readonly IEnvironmentValidator _validator;
        private readonly ISchemaManager _schemaManager;
        private readonly IPluginDeployer _pluginDeployer;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetupService"/> class.
        /// </summary>
        /// <param name="validator">The environment validator.</param>
        /// <param name="schemaManager">The schema manager.</param>
        /// <param name="pluginDeployer">The plugin deployer.</param>
        /// <param name="logger">The logger instance.</param>
        public SetupService(
            IEnvironmentValidator validator,
            ISchemaManager schemaManager,
            IPluginDeployer pluginDeployer,
            ILogger logger
        )
        {
            _validator = validator;
            _schemaManager = schemaManager;
            _pluginDeployer = pluginDeployer;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> IsEnvironmentReadyAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        )
        {
            return await _validator.IsEnvironmentReadyAsync(target, ct);
        }

        /// <inheritdoc />
        public async Task CreateSchemaAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            await _schemaManager.CreateSchemaAsync(target, progress, ct);
        }

        /// <inheritdoc />
        public async Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        )
        {
            await _pluginDeployer.DeployPluginAsync(
                target,
                pluginAssemblyPath,
                progress,
                ct
            );
        }
    }
}
