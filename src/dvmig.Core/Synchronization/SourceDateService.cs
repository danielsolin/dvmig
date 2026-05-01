using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Manages temporary 'dm_sourcedate' records in the target environment.
   /// </summary>
   public class SourceDateService : ISourceDateService
   {
      private readonly ILogger _logger;
      private bool? _isDatePreservationSupported;

      public SourceDateService(ILogger logger)
      {
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task CreateSourceDateRecordAsync(
         IDataverseProvider target,
         Entity sourceEntity,
         CancellationToken ct = default
      )
      {
         if (!await CheckSourceDateEntityExistsAsync(target, ct))
            return;

         bool hasDates =
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.CreatedOn) ||
            sourceEntity.Contains(
               SystemConstants.DataverseAttributes.ModifiedOn);

         if (!hasDates)
            return;

         var sourceDate = CreateSourceDateRecord(sourceEntity);

         try
         {
            await target.CreateAsync(sourceDate, ct);
         }
         catch (Exception ex)
         {
            _logger.Warning(
               ex,
               "Failed to create source date record for {Entity}:{Id}",
               sourceEntity.LogicalName,
               sourceEntity.Id
            );
         }
      }

      /// <inheritdoc />
      public async Task DeleteSourceDateRecordAsync(
         IDataverseProvider target,
         string logicalName,
         Guid entityId,
         CancellationToken ct = default
      )
      {
         if (!await CheckSourceDateEntityExistsAsync(target, ct))
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

      private async Task<bool> CheckSourceDateEntityExistsAsync(
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

      private Entity CreateSourceDateRecord(Entity entity)
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
