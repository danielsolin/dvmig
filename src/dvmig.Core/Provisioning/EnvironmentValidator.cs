using dvmig.Core.Interfaces;
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
                SystemConstants.SourceDate.EntityLogicalName,
                ct
            );
            if (md == null)
               return false;

            md = await target.GetEntityMetadataAsync(
                SystemConstants.MigrationFailure.EntityLogicalName,
                ct
            );
            if (md == null)
               return false;

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

            var assemblies = await target.RetrieveMultipleAsync(query, ct);
            var assembly = assemblies.Entities.FirstOrDefault();

            if (assembly == null)
               return false;

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
                assembly.Id
            );
            typeQuery.AddAttributeValue(
                SystemConstants.PluginRegistration.TypeName,
                $"{SystemConstants.AppConstants.PluginName}.DMPlugin"
            );

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);
            var pluginType = types.Entities.FirstOrDefault();

            if (pluginType == null)
               return false;

            var stepQuery = new QueryByAttribute(
                SystemConstants.PluginRegistration.StepEntity
            )
            {
               ColumnSet = new ColumnSet(
                   SystemConstants.PluginRegistration.StepId
               )
            };
            stepQuery.AddAttributeValue(
                SystemConstants.PluginRegistration.TypeId,
                pluginType.Id
            );

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
