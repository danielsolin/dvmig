using dvmig.Core.Interfaces;
using System.Collections.Concurrent;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of the metadata service that provides metadata
   /// retrieval, filtering, and caching to improve performance.
   /// </summary>
   public class MetadataService : IMetadataService
   {
      private readonly ILogger _logger;
      private readonly IDataverseProvider? _target;
      private readonly ConcurrentDictionary<string, EntityMetadata> _cache =
         new ConcurrentDictionary<string, EntityMetadata>();

      /// <summary>
      /// Initializes a new instance of the <see cref="MetadataService"/> class.
      /// </summary>
      /// <param name="logger">The logger instance.</param>
      /// <param name="target">
      /// Optional target provider for cached metadata operations.
      /// </param>
      public MetadataService(ILogger logger, IDataverseProvider? target = null)
      {
         _logger = logger;
         _target = target;
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         if (_cache.TryGetValue(entityLogicalName, out var meta))
            return meta;

         if (_target == null)
            throw new InvalidOperationException(
               "Target provider is required for cached metadata retrieval."
            );

         try
         {
            var newMeta = await _target
               .GetEntityMetadataAsync(entityLogicalName, ct);

            if (newMeta != null)
               _cache[entityLogicalName] = newMeta;

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
         CancellationToken ct = default
      )
      {
         var meta = await GetMetadataAsync(logicalName, ct);
         if (meta == null || meta.Attributes == null)
            return new ColumnSet(true);

         var whitelist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
         )
         {
            meta.PrimaryIdAttribute,
            meta.PrimaryNameAttribute ?? string.Empty,
            SystemConstants.DataverseAttributes.OwnerId,
            SystemConstants.DataverseAttributes.StateCode,
            SystemConstants.DataverseAttributes.StatusCode,
            SystemConstants.DataverseAttributes.CreatedOn,
            SystemConstants.DataverseAttributes.ModifiedOn,
            SystemConstants.DataverseAttributes.TransactionCurrencyId,
            SystemConstants.DataverseAttributes.ExchangeRate
         };

         var attributes = meta.Attributes
            .Where(a =>
               whitelist.Contains(a.LogicalName) ||
               a.AttributeType == AttributeTypeCode.PartyList ||
               (a.IsLogical == false &&
                a.IsValidForRead == true &&
                (a.IsValidForCreate == true ||
                 a.IsValidForUpdate == true)))
            .Select(a => a.LogicalName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToArray();

         if (attributes.Length == 0)
            return new ColumnSet(true);

         _logger.Debug(
            "Configured ColumnSet for {Entity} with {Count} attributes.",
            logicalName,
            attributes.Length
         );

         return new ColumnSet(attributes);
      }

      /// <inheritdoc />
      public async Task<List<EntityMetadata>> GetMigrationEntitiesAsync(
         IDataverseProvider provider,
         CancellationToken ct = default
      )
      {
         var request = new RetrieveAllEntitiesRequest
         {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
         };

         var response = (RetrieveAllEntitiesResponse)await
            provider.ExecuteAsync(request, ct);

         return response.EntityMetadata
            .Where(e =>
               (e.IsCustomEntity == true) &&
               e.IsIntersect == false &&
               e.IsValidForAdvancedFind == true &&
               !string.IsNullOrEmpty(
                  e.DisplayName?.UserLocalizedLabel?.Label))
            .OrderBy(e =>
               e.DisplayName?.UserLocalizedLabel?.Label ??
               e.LogicalName)
            .ToList();
      }

      /// <inheritdoc />
      public async Task<long> GetRecordCountAsync(
         IDataverseProvider provider,
         string logicalName,
         CancellationToken ct = default
      )
      {
         return await provider.GetRecordCountAsync(logicalName, ct);
      }

      /// <inheritdoc />
      public void ClearCache()
      {
         _cache.Clear();
      }
   }
}
