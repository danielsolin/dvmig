using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using dvmig.Providers;

namespace dvmig.Core
{
    public interface IDataPreservationManager
    {
        Task PreserveDatesAsync(
            Entity sourceEntity,
            CancellationToken ct = default);

        Task PreserveDatesBulkAsync(
            IEnumerable<Entity> entities,
            CancellationToken ct = default);
    }

    public class DataPreservationManager : IDataPreservationManager
    {
        private readonly IDataverseProvider _target;
        private readonly Serilog.ILogger _logger;
        private bool? _isSupported;

        public DataPreservationManager(
            IDataverseProvider target,
            Serilog.ILogger logger)
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
                    ct);

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
                    "for this session.");
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
                    sourceEntity.Id);
            }
        }

        public async Task PreserveDatesBulkAsync(
            IEnumerable<Entity> entities,
            CancellationToken ct = default)
        {
            if (!await CheckSupportAsync(ct))
            {
                return;
            }

            var sourceDates = entities
                .Where(e => e.Contains("createdon") ||
                            e.Contains("modifiedon"))
                .Select(CreateSourceDateEntity)
                .ToList();

            if (!sourceDates.Any())
            {
                return;
            }

            _logger.Information(
                "Bulk creating {Count} source date records",
                sourceDates.Count);

            var request = new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = false
                },
                Requests = new OrganizationRequestCollection()
            };

            foreach (var sd in sourceDates)
            {
                request.Requests.Add(new CreateRequest
                {
                    Target = sd
                });
            }

            await _target.ExecuteAsync(request, ct);
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
    }
}
