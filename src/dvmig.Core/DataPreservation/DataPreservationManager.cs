using dvmig.Core.Interfaces;
using dvmig.Core.Providers;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core.DataPreservation
{
    /// <summary>
    /// Manages the preservation of original creation and modification
    /// dates from a source environment during record migration. This is
    /// achieved by creating temporary 'dm_sourcedate' side-car records
    /// in the target environment, which a target-side plugin then uses
    /// to override the system-generated dates.
    /// </summary>
    public class DataPreservationManager : IDataPreservationManager
    {
        private readonly IDataverseProvider _target;
        private readonly ILogger _logger;
        private bool? _isSupported;

        /// <summary>
        /// Initializes a new instance of the 
        /// <see cref="DataPreservationManager"/> class.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="logger">The logger instance.</param>
        public DataPreservationManager(
            IDataverseProvider target,
            ILogger logger
        )
        {
            _target = target;
            _logger = logger;
        }

        /// <summary>
        /// Checks whether the target environment supports date preservation 
        /// by verifying the existence of the 'dm_sourcedate' custom entity.
        /// The result is cached for the lifetime of the manager instance.
        /// </summary>
        /// <param name="ct">
        /// A cancellation token that can be used to cancel the operation.
        /// </param>
        /// <returns>
        /// True if date preservation is supported; otherwise, false.
        /// </returns>
        private async Task<bool> CheckSupportAsync(CancellationToken ct)
        {
            if (_isSupported.HasValue)
            {
                return _isSupported.Value;
            }

            try
            {
                var meta = await _target.GetEntityMetadataAsync(
                    Constants.SourceDate.EntityLogicalName,
                    ct
                );

                _isSupported = meta != null;
            }
            catch
            {
                _isSupported = false;
            }

            if (_isSupported == false)
            {
                _logger.Warning(
                    "Date preservation entity '{Entity}' not found " +
                    "on target. Date preservation will be disabled " +
                    "for this session.",
                    Constants.SourceDate.EntityLogicalName
                );
            }

            return _isSupported.Value;
        }

        /// <inheritdoc />
        public async Task PreserveDatesAsync(
            Entity sourceEntity,
            CancellationToken ct = default
        )
        {
            if (!await CheckSupportAsync(ct))
            {
                return;
            }

            if (!sourceEntity.Contains("createdon") &&
                !sourceEntity.Contains("modifiedon"))
            {
                return;
            }

            var sourceDate = CreateSourceDateEntity(sourceEntity);

            try
            {
                await _target.CreateAsync(sourceDate, ct);
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

        /// <summary>
        /// Creates a 'dm_sourcedate' entity instance populated with the 
        /// original dates and identifying information from the source entity.
        /// </summary>
        /// <param name="entity">The source entity.</param>
        /// <returns>A populated 'dm_sourcedate' entity.</returns>
        private Entity CreateSourceDateEntity(Entity entity)
        {
            var sourceDate = new Entity(
                Constants.SourceDate.EntityLogicalName
            );

            sourceDate[Constants.SourceDate.EntityId] =
                entity.Id.ToString();

            sourceDate[Constants.SourceDate.EntityLogicalNameAttr] =
                entity.LogicalName.ToLower();

            if (entity.Contains("createdon"))
            {
                sourceDate[Constants.SourceDate.CreatedDate] =
                    entity["createdon"];
            }

            if (entity.Contains("modifiedon"))
            {
                sourceDate[Constants.SourceDate.ModifiedDate] =
                    entity["modifiedon"];
            }

            return sourceDate;
        }

        /// <inheritdoc />
        public async Task DeleteSourceDateAsync(
            string logicalName,
            Guid entityId,
            CancellationToken ct = default
        )
        {
            if (!await CheckSupportAsync(ct))
            {
                return;
            }

            try
            {
                var entityName = Constants.SourceDate.EntityLogicalName;
                var primaryId = Constants.SourceDate.PrimaryId;
                var sourceEntityId = Constants.SourceDate.EntityId;
                var logicalNameAttr =
                    Constants.SourceDate.EntityLogicalNameAttr;

                var fetchXml = $@"
                    <fetch version='1.0' output-format='xml-platform' 
                           mapping='logical' distinct='false' count='1'>
                      <entity name='{entityName}'>
                        <attribute name='{primaryId}' />
                        <filter type='and'>
                          <condition attribute='{sourceEntityId}' 
                            operator='eq' value='{entityId}' />
                          <condition attribute='{logicalNameAttr}' 
                            operator='eq' value='{logicalName.ToLower()}' />
                        </filter>
                      </entity>
                    </fetch>";

                var result = await _target.RetrieveMultipleAsync(
                    new FetchExpression(fetchXml),
                    ct
                );

                if (result.Entities.Any())
                {
                    await _target.DeleteAsync(
                        Constants.SourceDate.EntityLogicalName,
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
    }
}
