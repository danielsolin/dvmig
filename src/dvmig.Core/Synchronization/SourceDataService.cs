using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="ISourceDataService"/> that manages
   /// temporary 'dm_sourcedata' records in the target environment to
   /// facilitate audit data preservation.
   /// </summary>
   public class SourceDataService : ISourceDataService
   {
      private readonly ILogger _logger;
      private bool? _isSourceDataSupported;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="SourceDataService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      public SourceDataService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task CreateSourceDataRecordAsync(
         IDataverseProvider target,
         Entity sourceEntity,
         IUserResolver userResolver,
         CancellationToken ct = default
      )
      {
         if (!await CheckSourceDataEntityExistsAsync(target, ct))
            return;

         bool hasAuditData =
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.CreatedOn) ||
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.ModifiedOn) ||
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.CreatedBy) ||
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.ModifiedBy);

         if (!hasAuditData)
            return;

         var sourceData = await CreateSourceDataRecordAsync(
            sourceEntity,
            userResolver,
            ct
         );

         try
         {
            await target.CreateAsync(sourceData, ct);
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to create source data record for {Entity}:{Id}",
               sourceEntity.LogicalName,
               sourceEntity.Id
            );
         }
      }

      /// <inheritdoc />
      public async Task DeleteSourceDataRecordAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CancellationToken ct = default
      )
      {
         if (!await CheckSourceDataEntityExistsAsync(target, ct))
            return;

         try
         {
            var entityName = SystemConstants.SourceData.EntityLogicalName;
            var primaryId = SystemConstants.SourceData.PrimaryId;
            var sourceEntityId = SystemConstants.SourceData.EntityId;
            var logicalNameAttr =
               SystemConstants.SourceData.EntityLogicalNameAttr;

            var fetchXml = $@"
                    <fetch version='1.0' output-format='xml-platform' 
                           mapping='logical' distinct='false' count='1'>
                      <entity name='{entityName}'>
                        <attribute name='{primaryId}' />
                        <filter type='and'>
                          <condition attribute='{sourceEntityId}' 
                            operator='eq' value='{entityId}' />
                          <condition attribute='{logicalNameAttr}' 
                            operator='eq' 
                            value='{logicalName.ToLowerInvariant()}' />
                        </filter>
                      </entity>
                    </fetch>";

            var result = await target.RetrieveMultipleAsync(
               new FetchExpression(fetchXml),
               ct
            );

            if (result.Entities.Any())
               await target.DeleteAsync(
                  SystemConstants.SourceData.EntityLogicalName,
                  result.Entities[0].Id,
                  ct
               );
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to delete source data record for {Entity}:{Id}",
               logicalName,
               entityId
            );
         }
      }

      private async Task<bool> CheckSourceDataEntityExistsAsync(
         IDataverseProvider target,
         CancellationToken ct
      )
      {
         if (_isSourceDataSupported.HasValue)
            return _isSourceDataSupported.Value;

         try
         {
            var meta = await target.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               ct
            );

            _isSourceDataSupported = meta != null;
         }
         catch
         {
            _isSourceDataSupported = false;
         }

         if (_isSourceDataSupported == false)
         {
            _logger.Warning(
               "Source data preservation entity '{Entity}' not found " +
               "on target. Audit data preservation will be disabled " +
               "for this session.",
               SystemConstants.SourceData.EntityLogicalName
            );
         }

         return _isSourceDataSupported.Value;
      }

      private async Task<Entity> CreateSourceDataRecordAsync(
         Entity entity,
         IUserResolver userResolver,
         CancellationToken ct
      )
      {
         var sourceData = new Entity(
            SystemConstants.SourceData.EntityLogicalName
         );

         sourceData[SystemConstants.SourceData.EntityId] =
            entity.Id.ToString();

         sourceData[SystemConstants.SourceData.EntityLogicalNameAttr] =
            entity.LogicalName.ToLowerInvariant();

         if (entity.Contains(SystemConstants.DataverseAttributes.CreatedOn))
            sourceData[SystemConstants.SourceData.CreatedOn] =
               entity[SystemConstants.DataverseAttributes.CreatedOn];

         if (entity.Contains(SystemConstants.DataverseAttributes.ModifiedOn))
            sourceData[SystemConstants.SourceData.ModifiedOn] =
               entity[SystemConstants.DataverseAttributes.ModifiedOn];

         if (entity.Contains(SystemConstants.DataverseAttributes.CreatedBy))
         {
            var sourceUser = entity.GetAttributeValue<EntityReference>(
               SystemConstants.DataverseAttributes.CreatedBy
            );

            var mappedUser = await userResolver.MapUserAsync(sourceUser, ct);
            if (mappedUser != null)
            {
               sourceData[SystemConstants.SourceData.CreatedBy] =
                  mappedUser.Id.ToString();
            }
            else
            {
               _logger.Warning(
                  "Could not map CreatedBy user {UserId} for {Entity}:{Id}. " +
                  "User attribution for this field will default to the current caller.",
                  sourceUser.Id,
                  entity.LogicalName,
                  entity.Id
               );
            }
         }

         if (entity.Contains(SystemConstants.DataverseAttributes.ModifiedBy))
         {
            var sourceUser = entity.GetAttributeValue<EntityReference>(
               SystemConstants.DataverseAttributes.ModifiedBy
            );

            var mappedUser = await userResolver.MapUserAsync(sourceUser, ct);
            if (mappedUser != null)
            {
               sourceData[SystemConstants.SourceData.ModifiedBy] =
                  mappedUser.Id.ToString();
            }
            else
            {
               _logger.Warning(
                  "Could not map ModifiedBy user {UserId} for {Entity}:{Id}. " +
                  "User attribution for this field will default to the current caller.",
                  sourceUser.Id,
                  entity.LogicalName,
                  entity.Id
               );
            }
         }

         return sourceData;
      }
   }
}
