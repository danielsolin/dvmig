using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core
{
    public class DataPreservationManager : IDataPreservationManager
    {
        private readonly IDataverseProvider _target;
        private readonly ILogger _logger;
        private bool? _isSupported;

        public DataPreservationManager(
            IDataverseProvider target,
            ILogger logger)
        {
            _target = target;
            _logger = logger;
        }

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

        public async Task PreserveDatesAsync(
            Entity sourceEntity,
            CancellationToken ct = default)
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

        public async Task DeleteSourceDateAsync(
            string logicalName,
            Guid entityId,
            CancellationToken ct = default)
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
