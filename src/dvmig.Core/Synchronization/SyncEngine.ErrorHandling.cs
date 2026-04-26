using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
    public partial class SyncEngine
    {
        /// <summary>
        /// Prepares and retries the synchronization of an entity.
        /// </summary>
        /// <param name="entity">The entity record to retry.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the retry was successful; otherwise, false.
        /// </returns>
        private async Task<bool> RetryEntityAsync(
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var metadata = await GetMetadataAsync(entity.LogicalName, ct);
            if (metadata == null)
            {
                return false;
            }

            var prepared = await PrepareEntityForTargetAsync(
                entity,
                metadata,
                options,
                ct
            );

            if (metadata?.IsIntersect == true)
            {
                var (success, _) = await SyncIntersectEntityAsync(
                    prepared,
                    options,
                    progress,
                    ct
                );

                return success;
            }

            var (created, _) = await CreateWithFixStrategyAsync(
                prepared,
                options,
                progress,
                ct
            );

            return created;
        }

        /// <summary>
        /// Identifies an attribute that caused a synchronization failure,
        /// removes it from the entity, and retries the synchronization.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="entity">The entity record.</param>
        /// <param name="options">The synchronization configuration.</param>
        /// <param name="progress">The progress reporter.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// True if the attribute was stripped and the retry was successful;
        /// otherwise, false.
        /// </returns>
        private async Task<bool> StripAttributeAndRetryAsync(
            Exception ex,
            Entity entity,
            SyncOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var match = Regex.Match(
                ex.Message,
                @"'(\w+)'"
            );

            if (match.Success)
            {
                var attrName = match.Groups[1].Value;
                if (entity.Attributes.Contains(attrName))
                {
                    _logger.Warning(
                        "Stripping attribute '{Attr}' for {Key}:{Id}",
                        attrName,
                        entity.LogicalName,
                        entity.Id
                    );

                    progress?.Report(
                        $"Stripping attribute '{attrName}' and retrying..."
                    );

                    entity.Attributes.Remove(attrName);

                    var (success, _) = await CreateWithFixStrategyAsync(
                        entity,
                        options,
                        progress,
                        ct
                    );

                    return success;
                }
            }

            return false;
        }
    }
}
