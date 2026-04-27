using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
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
                var md = await target.GetEntityMetadataAsync(
                    Constants.SourceDate.EntityLogicalName,
                    ct
                );
                if (md == null)
                    return false;

                md = await target.GetEntityMetadataAsync(
                    Constants.MigrationFailure.EntityLogicalName,
                    ct
                );
                if (md == null)
                    return false;

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

                var stepQuery = new QueryByAttribute("sdkmessageprocessingstep")
                {
                    ColumnSet = new ColumnSet("sdkmessageprocessingstepid")
                };
                stepQuery.AddAttributeValue("plugintypeid", pluginType.Id);

                var steps = await target.RetrieveMultipleAsync(stepQuery, ct);

                // Both Create and Update steps should be present:
                return steps.Entities.Count >= 2;
            }
            catch
            {
                return false;
            }
        }
    }
}
