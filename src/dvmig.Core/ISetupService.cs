using dvmig.Providers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dvmig.Core
{
    public interface ISetupService
    {
        Task<bool> IsEnvironmentReadyAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        );

        Task CreateSchemaAsync(
            IDataverseProvider target,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );

        Task DeployPluginAsync(
            IDataverseProvider target,
            string pluginAssemblyPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        );
    }
}