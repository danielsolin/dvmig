using System.Collections.Concurrent;
using dvmig.Providers;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Serilog;

namespace dvmig.Core.Synchronization
{
    /// <summary>
    /// Implementation of <see cref="IMetadataCache"/> that provides metadata
    /// retrieval with caching to improve performance.
    /// </summary>
    public class MetadataCache : IMetadataCache
    {
        private readonly IDataverseProvider _target;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, EntityMetadata> _cache =
            new ConcurrentDictionary<string, EntityMetadata>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MetadataCache"/> class.
        /// </summary>
        /// <param name="target">The target Dataverse provider.</param>
        /// <param name="logger">The logger instance.</param>
        public MetadataCache(
            IDataverseProvider target,
            ILogger logger)
        {
            _target = target;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<EntityMetadata?> GetMetadataAsync(
            string entityLogicalName,
            CancellationToken ct = default)
        {
            if (_cache.TryGetValue(entityLogicalName, out var meta))
            {
                return meta;
            }

            try
            {
                var newMeta = await _target
                    .GetEntityMetadataAsync(entityLogicalName, ct);

                if (newMeta != null)
                {
                    _cache[entityLogicalName] = newMeta;
                }

                return newMeta;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    "Could not fetch metadata for {Entity}: {Msg}",
                    entityLogicalName,
                    ex.Message
                );

                return null;
            }
        }

        /// <inheritdoc />
        public async Task<ColumnSet> GetValidColumnsAsync(
            string logicalName,
            CancellationToken ct = default)
        {
            var meta = await GetMetadataAsync(logicalName, ct);
            if (meta == null || meta.Attributes == null)
            {
                return new ColumnSet(true);
            }

            // Safety Whitelist: These columns MUST be included if they exist
            var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                meta.PrimaryIdAttribute,
                meta.PrimaryNameAttribute ?? string.Empty,
                "ownerid",
                "statecode",
                "statuscode",
                "createdon",
                "modifiedon",
                "transactioncurrencyid",
                "exchangerate"
            };

            // Filter for attributes that are valid for reading and NOT purely
            // logical/calculated to avoid performance issues.
            var attributes = meta.Attributes
                .Where(a =>
                    whitelist.Contains(a.LogicalName) ||
                    (a.IsLogical == false &&
                     a.IsValidForRead == true &&
                     (a.IsValidForCreate == true ||
                      a.IsValidForUpdate == true)))
                .Select(a => a.LogicalName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToArray();

            if (attributes.Length == 0)
            {
                return new ColumnSet(true);
            }

            _logger.Debug(
                "Configured ColumnSet for {Entity} with {Count} attributes.",
                logicalName,
                attributes.Length
            );

            return new ColumnSet(attributes);
        }

        /// <inheritdoc />
        public void ClearCache()
        {
            _cache.Clear();
        }
    }
}
