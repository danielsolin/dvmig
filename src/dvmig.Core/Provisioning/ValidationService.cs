using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Provisioning
{
   /// <summary>
   /// Implementation of <see cref="IValidationService"/> that validates 
   /// environment readiness.
   /// </summary>
   public class ValidationService : IValidationService
   {
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
               SystemConstants.MigrationFailure.EntityLogicalName,
               ct
            );

            if (failureMeta == null)
               return false;

            // 2. Check Source Date Entity
            var sourceDateMeta = await target.GetEntityMetadataAsync(
               SystemConstants.SourceDate.EntityLogicalName,
               ct
            );

            if (sourceDateMeta == null)
               return false;

            // 3. Check Plugin Assembly
            var assemblyQuery = new QueryByAttribute(
               SystemConstants.PluginRegistration.AssemblyEntity
            )
            {
               ColumnSet = new ColumnSet(
                  SystemConstants.PluginRegistration.AssemblyId
               )
            };
            assemblyQuery.AddAttributeValue(
               SystemConstants.PluginRegistration.AssemblyName,
               SystemConstants.AppConstants.PluginName
            );

            var assemblies = await target.RetrieveMultipleAsync(
               assemblyQuery,
               ct
            );

            if (!assemblies.Entities.Any())
               return false;

            // 4. Check Plugin Type
            var typeQuery = new QueryByAttribute(
               SystemConstants.PluginRegistration.TypeEntity
            )
            {
               ColumnSet = new ColumnSet(
                  SystemConstants.PluginRegistration.TypeId
               )
            };
            typeQuery.AddAttributeValue(
               SystemConstants.PluginRegistration.TypeName,
               $"{SystemConstants.AppConstants.PluginName}.DMPlugin"
            );

            var types = await target.RetrieveMultipleAsync(typeQuery, ct);

            if (!types.Entities.Any())
               return false;

            var typeId = types.Entities.First().Id;

            // 5. Check Plugin Steps (Create & Update)
            var stepQuery = new QueryByAttribute(
               SystemConstants.PluginRegistration.StepEntity
            )
            {
               ColumnSet = new ColumnSet(
                  SystemConstants.PluginRegistration.MessageName
               )
            };
            stepQuery.AddAttributeValue(
               SystemConstants.PluginRegistration.EventHandler,
               typeId
            );

            var steps = await target.RetrieveMultipleAsync(stepQuery, ct);

            bool hasCreate = steps.Entities.Any(e =>
               e.GetAttributeValue<string>(
                  SystemConstants.PluginRegistration.MessageName
               )?.Contains("Create") == true
            );

            bool hasUpdate = steps.Entities.Any(e =>
               e.GetAttributeValue<string>(
                  SystemConstants.PluginRegistration.MessageName
               )?.Contains("Update") == true
            );

            return hasCreate && hasUpdate;
         }
         catch
         {
            return false;
         }
      }
   }
}
