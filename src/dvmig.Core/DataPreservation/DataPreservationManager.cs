using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core.DataPreservation
{
    /// <summary>
    /// Manages the preservation of original creation and modification dates 
    /// from a source environment during record migration. This is achieved 
    /// by creating temporary 'dm_sourcedate' side-car records in the target 
    /// environment, which a target-side plugin then uses to override the 
    /// system-generated dates.
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
                    "dm_sourcedate",
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
                    "Date preservation entity 'dm_sourcedate' not found " +
                    "on target. Date preservation will be disabled " +
                    "for this session."
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
            var sourceDate = new Entity("dm_sourcedate");

            sourceDate["dm_sourceentityid"] = entity.Id.ToString();

            sourceDate["dm_sourceentitylogicalname"] =
                entity.LogicalName.ToLower();

            if (entity.Contains("createdon"))
            {
                sourceDate["dm_sourcecreateddate"] = entity["createdon"];
            }

            if (entity.Contains("modifiedon"))
            {
                sourceDate["dm_sourcemodifieddate"] = entity["modifiedon"];
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
                var fetchXml = $@"
                    <fetch version='1.0' output-format='xml-platform' 
                           mapping='logical' distinct='false' count='1'>
                      <entity name='dm_sourcedate'>
                        <attribute name='dm_sourcedateid' />
                        <filter type='and'>
                          <condition attribute='dm_sourceentityid' 
                            operator='eq' value='{entityId}' />
                          <condition attribute='dm_sourceentitylogicalname' 
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
                        "dm_sourcedate",
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
