using dvmig.Core.Interfaces;
using dvmig.Core.Logging;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core.Provisioning
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
      private bool? _isDatePreservationSupported;

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
         IProgress<string>? progress = null,
         CancellationToken ct = default
      )
      {
         var assemblyPath = Path.Combine(
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

         if (!File.Exists(assemblyPath))
         {
            var msg = $"Plugin assembly not found at {assemblyPath}. " +
                      "Cannot proceed with installation.";
            _logger.Error(msg);
            throw new FileNotFoundException(msg, assemblyPath);
         }

         await _pluginDeployer.DeployPluginAsync(
            target,
            assemblyPath,
            progress,
            ct
         );
      }

      /// <inheritdoc />
      public async Task CleanEnvironmentAsync(
         IDataverseProvider target,
         IProgress<string>? progress = null,
         CancellationToken ct = default
      )
      {
         _logger.Information(progress, "Cleaning target environment...");

         // 1. Remove Plugin
         await _pluginDeployer.RemovePluginAsync(target, progress, ct);

         // 2. Drop Schema
         await _schemaManager.DropSchemaAsync(target, progress, ct);

         _logger.Information(progress, "Environment cleanup completed.");
      }

      /// <inheritdoc />
      public async Task PreserveDatesAsync(
         IDataverseProvider target,
         Entity sourceEntity,
         CancellationToken ct = default
      )
      {
         if (!await CheckDatePreservationSupportAsync(target, ct))
            return;

         bool hasDates =
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.CreatedOn) ||
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.ModifiedOn);

         if (!hasDates)
            return;

         var sourceDate = CreateSourceDateEntity(sourceEntity);

         try
         {
            await target.CreateAsync(sourceDate, ct);
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to create source date for {Entity}:{Id}",
               sourceEntity.LogicalName,
               sourceEntity.Id
            );
         }
      }

      /// <inheritdoc />
      public async Task DeleteSourceDateAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CancellationToken ct = default
      )
      {
         if (!await CheckDatePreservationSupportAsync(target, ct))
            return;

         try
         {
            var entityName = SystemConstants.SourceDate.EntityLogicalName;
            var primaryId = SystemConstants.SourceDate.PrimaryId;
            var sourceEntityId = SystemConstants.SourceDate.EntityId;
            var logicalNameAttr =
               SystemConstants.SourceDate.EntityLogicalNameAttr;

            var fetchXml = $@"
                    <fetch version='1.0' output-format='xml-platform' 
                           mapping='logical' distinct='false' count='1'>
                      <entity name='{entityName}'>
                        <attribute name='{primaryId}' />
                        <filter type='and'>
                          <condition attribute='{sourceEntityId}' 
                            operator='eq' value='{entityId}' />
                          <condition attribute='{logicalNameAttr}' 
                            operator='eq' value='{logicalName.ToLowerInvariant()}' />
                        </filter>
                      </entity>
                    </fetch>";

            var result = await target.RetrieveMultipleAsync(
               new FetchExpression(fetchXml),
               ct
            );

            if (result.Entities.Any())
            {
               await target.DeleteAsync(
                  SystemConstants.SourceDate.EntityLogicalName,
                  result.Entities[0].Id,
                  ct
               );
            }
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to delete source date record for {Entity}:{Id}",
               logicalName,
               entityId
            );
         }
      }

      private async Task<bool> CheckDatePreservationSupportAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         if (_isDatePreservationSupported.HasValue)
            return _isDatePreservationSupported.Value;

         try
         {
            var meta = await target.GetEntityMetadataAsync(
               SystemConstants.SourceDate.EntityLogicalName,
               ct
            );

            _isDatePreservationSupported = meta != null;
         }
         catch
         {
            _isDatePreservationSupported = false;
         }

         if (_isDatePreservationSupported == false)
         {
            _logger.Warning(
               "Date preservation entity '{Entity}' not found " +
               "on target. Date preservation will be disabled " +
               "for this session.",
               SystemConstants.SourceDate.EntityLogicalName
            );
         }

         return _isDatePreservationSupported.Value;
      }

      private Entity CreateSourceDateEntity(Entity entity)
      {
         var sourceDate = new Entity(
            SystemConstants.SourceDate.EntityLogicalName
         );

         sourceDate[SystemConstants.SourceDate.EntityId] =
            entity.Id.ToString();

         sourceDate[SystemConstants.SourceDate.EntityLogicalNameAttr] =
            entity.LogicalName.ToLowerInvariant();

         if (entity.Contains(SystemConstants.DataverseAttributes.CreatedOn))
            sourceDate[SystemConstants.SourceDate.CreatedDate] =
               entity[SystemConstants.DataverseAttributes.CreatedOn];

         if (entity.Contains(SystemConstants.DataverseAttributes.ModifiedOn))
            sourceDate[SystemConstants.SourceDate.ModifiedDate] =
               entity[SystemConstants.DataverseAttributes.ModifiedOn];

         return sourceDate;
      }
   }
}
