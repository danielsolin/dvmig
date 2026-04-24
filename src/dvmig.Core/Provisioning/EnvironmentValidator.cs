using dvmig.Providers;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Provisioning
{
    /// <summary>
    /// Validates if the target Dataverse environment is ready for migration.
    /// </summary>
    public class EnvironmentValidator : IEnvironmentValidator
    {
        /// <inheritdoc />
        public async Task<bool> IsEnvironmentReadyAsync(
            IDataverseProvider target,
            CancellationToken ct = default
        )
        {
            try
            {
                var meta = await target.GetEntityMetadataAsync(
                    "dm_sourcedate",
                    ct
                );

                if (meta == null)
                {
                    return false;
                }

                // 1. Check for plugin assembly
                var query = new QueryByAttribute("pluginassembly")
                {
                    ColumnSet = new ColumnSet("pluginassemblyid")
                };
                query.AddAttributeValue("name", "dvmig.Plugins");

                var assemblies = await target.RetrieveMultipleAsync(query, ct);
                var assembly = assemblies.Entities.FirstOrDefault();

                if (assembly == null)
                {
                    return false;
                }

                // 2. Check for plugin type
                var typeQuery = new QueryByAttribute("plugintype")
                {
                    ColumnSet = new ColumnSet("plugintypeid")
                };
                typeQuery.AddAttributeValue("pluginassemblyid", assembly.Id);
                typeQuery.AddAttributeValue(
                    "typename",
                    "dvmig.Plugins.DMPlugin"
                );

                var types = await target.RetrieveMultipleAsync(typeQuery, ct);
                var pluginType = types.Entities.FirstOrDefault();

                if (pluginType == null)
                {
                    return false;
                }

                // 3. Check for plugin step
                var stepQuery = new QueryByAttribute("sdkmessageprocessingstep")
                {
                    ColumnSet = new ColumnSet("sdkmessageprocessingstepid")
                };
                stepQuery.AddAttributeValue("plugintypeid", pluginType.Id);

                var steps = await target.RetrieveMultipleAsync(stepQuery, ct);

                // We require both Create and Update steps to be registered
                return steps.Entities.Count >= 2;
            }
            catch
            {
                return false;
            }
        }
    }
}
