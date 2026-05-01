using dvmig.Core.Interfaces;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Implementation of <see cref="IDependencyResolver"/> that resolves
   /// missing dependencies during entity synchronization.
   /// </summary>
   public class DependencyResolver : IDependencyResolver
   {
      private readonly IDataverseProvider _source;
      private readonly ILogger _logger;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="DependencyResolver"/> class.
      /// </summary>
      /// <param name="source">The source Dataverse provider.</param>
      /// <param name="logger">The logger instance.</param>
      public DependencyResolver(
          IDataverseProvider source,
          ILogger logger)
      {
         _source = source;
         _logger = logger;
      }

      /// <inheritdoc />
      public async Task<bool> ResolveMissingDependencyAsync(
          Exception ex,
          Entity entity,
          SyncOptions options,
          CancellationToken ct = default,
          Func<Entity, SyncOptions, CancellationToken,
              Task<(bool Success, string? FailureMessage)>>?
              syncRecordFunc = null,
          Func<Entity, SyncOptions, CancellationToken,
              Task<bool>>? retryEntityFunc = null,
          Func<Entity, CancellationToken, Task<Guid?>>?
              findExistingFunc = null,
          ConcurrentDictionary<string, Guid>? idMappingCache = null,
          ConcurrentDictionary<string, HashSet<string>>?
              triedDependencies = null)
      {
         // More robust regex to handle both:
         // "Account with Id=GUID does not exist"
         // "Entity 'transactioncurrency' With Id = GUID Does Not Exist"
         var pattern =
             @"(?:Entity )?'?(\w+)'? [Ww]ith Id\s*=\s*([a-fA-F0-9-]+)";

         var match = Regex.Match(
             ex.Message,
             pattern,
             RegexOptions.IgnoreCase
         );

         if (!match.Success)
            return false;

         var missingType = match.Groups[1].Value.ToLower();
         var missingId = Guid.Parse(match.Groups[2].Value);
         var recordKey = $"{entity.LogicalName}:{entity.Id}";
         var dependencyKey = $"{missingType}:{missingId}";

         var tried = triedDependencies?.GetOrAdd(
             recordKey,
             _ => new HashSet<string>()
         );

         if (tried != null && tried.Contains(dependencyKey))
         {
            _logger.Warning(
                "Already tried to resolve {Dep} for {Record}. " +
                "Falling back to stripping logic.",
                dependencyKey,
                recordKey
            );
         }
         else
         {
            tried?.Add(dependencyKey);

            _logger.Information(
                "Missing dependency {Type}:{Id} detected. " +
                "Attempting to resolve.",
                missingType,
                missingId
            );

            _logger.Information(
                $"Resolving missing dependency: {missingType}:{missingId}"
            );

            var missingRecord = await _source.RetrieveAsync(
                missingType,
                missingId,
                null,
                ct
            );

            if (missingRecord != null)
            {
               // Dynamic Mapping: Try to find by business key first
               var targetId = findExistingFunc != null
                   ? await findExistingFunc(missingRecord, ct)
                   : null;

               if (targetId.HasValue && idMappingCache != null)
               {
                  _logger.Information(
                      "Found {Type} by business key. " +
                      "Mapping {SourceId} -> {TargetId}",
                      missingType,
                      missingId,
                      targetId.Value
                  );

                  idMappingCache[dependencyKey] = targetId.Value;

                  return retryEntityFunc != null
                      ? await retryEntityFunc(
                          entity,
                          options,
                          ct
                      )
                      : false;
               }

               // Normal Sync: Try to sync the record over
               var (success, _) = syncRecordFunc != null
                   ? await syncRecordFunc(
                       missingRecord,
                       options,
                       ct
                   )
                   : (false, "No sync function provided");

               if (success && retryEntityFunc != null)
               {
                  return await retryEntityFunc(
                      entity,
                      options,
                      ct
                  );
               }
            }
         }

         // Fallback: If auto-sync of dependency failed (or not found),
         // strip the attribute and retry the parent record.
         if (options.StripMissingDependencies)
         {
            var attrToStrip = entity.Attributes
                .FirstOrDefault(a =>
                    a.Value is EntityReference er &&
                    er.LogicalName == missingType &&
                    er.Id == missingId
                ).Key;

            if (!string.IsNullOrEmpty(attrToStrip))
            {
               _logger.Warning(
                   "Dependency resolution failed for {Type}:{Id}. " +
                   "Stripping attribute '{Attr}' from " +
                   "{ParentType}:{ParentId} and retrying.",
                   missingType,
                   missingId,
                   attrToStrip,
                   entity.LogicalName,
                   entity.Id
               );

               _logger.Information(
                   $"Dependency resolution failed. Stripping " +
                   $"'{attrToStrip}' and retrying..."
               );

               entity.Attributes.Remove(attrToStrip);

               // Actually execute the retry now that the attribute
               // is stripped.
               if (retryEntityFunc != null)
                  return await retryEntityFunc(
                      entity,
                      options,
                      ct
                  );

               return true;
            }
         }

         return false;
      }

      /// <inheritdoc />
      public async Task<bool> ResolveSqlDependencyAsync(
          string message,
          Entity entity,
          SyncOptions options,
          CancellationToken ct = default,
          Func<Entity, SyncOptions, CancellationToken,
              Task<(bool Success, string? FailureMessage)>>?
              syncRecordFunc = null,
          Func<Entity, SyncOptions, CancellationToken,
              Task<bool>>? retryEntityFunc = null,
          Func<Entity, CancellationToken, Task<Guid?>>?
              findExistingFunc = null,
          ConcurrentDictionary<string, Guid>? idMappingCache = null)
      {
         // Extract column name from message (e.g., column
         // 'TransactionCurrencyId')
         var match = Regex.Match(message, @"column '(\w+)'");
         if (!match.Success)
            return false;

         var columnName = match.Groups[1].Value.ToLower();

         // Find the attribute in the entity
         var attr = entity.Attributes
             .FirstOrDefault(a => a.Key.ToLower() == columnName);

         if (attr.Value is EntityReference er)
         {
            _logger.Information(
                "Detected missing SQL dependency for {Attr} on {Entity}. " +
                "Attempting to resolve {DepType}:{DepId}",
                columnName,
                entity.LogicalName,
                er.LogicalName,
                er.Id
            );

            _logger.Information(
                $"Resolving SQL dependency: {er.LogicalName}:{er.Id}"
            );

            var missingRecord = await _source.RetrieveAsync(
                er.LogicalName,
                er.Id,
                null,
                ct
            );

            if (missingRecord != null)
            {
               // Dynamic Mapping: Try to find by business key first
               var targetId = findExistingFunc != null
                   ? await findExistingFunc(missingRecord, ct)
                   : null;

               if (targetId.HasValue && idMappingCache != null)
               {
                  _logger.Information(
                      "Found {Type} by business key. " +
                      "Mapping {SourceId} -> {TargetId}",
                      er.LogicalName,
                      er.Id,
                      targetId.Value
                  );

                  idMappingCache[$"{er.LogicalName}:{er.Id}"] =
                      targetId.Value;

                  return retryEntityFunc != null
                      ? await retryEntityFunc(
                          entity,
                          options,
                          ct
                      )
                      : false;
               }

               // Re-use the existing logic to sync the missing record
               var (success, _) = syncRecordFunc != null
                   ? await syncRecordFunc(
                       missingRecord,
                       options,
                       ct
                   )
                   : (false, "No sync function provided");

               if (success && retryEntityFunc != null)
               {
                  return await retryEntityFunc(
                      entity,
                      options,
                      ct
                  );
               }
            }
         }

         return false;
      }
   }
}
