using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the modern
   /// <see cref="ServiceClient"/> from the PowerPlatform.Dataverse.Client SDK.
   /// </summary>
   public class DataverseProvider : IDataverseProvider, IDisposable
   {
      private readonly ServiceClient _client;
      private readonly AsyncRetryPolicy _retryPolicy;

      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <inheritdoc />
      public bool IsLegacy => false;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="DataverseProvider"/> class.
      /// </summary>
      /// <param name="connectionString">
      /// The connection string to the Dataverse environment.
      /// </param>
      public DataverseProvider(string connectionString)
      {
         ConnectionString = connectionString;
         _client = new ServiceClient(connectionString);

         if (!_client.IsReady)
            throw new Exception(
               $"Dataverse connection failed: {_client.LastError}"
            );

         _retryPolicy = Policy
            .Handle<Exception>(EntityHelper.IsTransientError)
            .WaitAndRetryAsync(
               5,
               attempt => TimeSpan.FromSeconds(
                  Math.Min(Math.Pow(2, attempt + 2), 30)
               )
            );
      }

      private async Task<T> ExecuteWithRetryAsync<T>(
         Func<CancellationToken, Task<T>> action,
         CancellationToken ct
      )
      {
         return await _retryPolicy.ExecuteAsync(
            async () => await action(ct)
         );
      }

      private async Task ExecuteWithRetryAsync(
         Func<CancellationToken, Task> action,
         CancellationToken ct
      )
      {
         await _retryPolicy.ExecuteAsync(
            async () => await action(ct)
         );
      }

      /// <inheritdoc />
      public async Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               try
               {
                  var columnSet = columns == null
                     ? new ColumnSet(true)
                     : new ColumnSet(columns);

                  return await _client.RetrieveAsync(
                     entityLogicalName,
                     id,
                     columnSet,
                     token
                  );
               }
               catch (Exception ex)
               {
                  if (ex.IsNotFoundException())
                     return null;

                  throw;
               }
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               try
               {
                  var response = await _client.ExecuteAsync(
                     new RetrieveEntityRequest
                     {
                        LogicalName = entityLogicalName,
                        EntityFilters = EntityFilters.Attributes
                     },
                     token
                  ) as RetrieveEntityResponse;

                  return response?.EntityMetadata;
               }
               catch
               {
                  // If entity doesn't exist, RetrieveEntityRequest throws.
                  // We return null to indicate missing metadata.

                  return null;
               }
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  using var clonedClient = _client.Clone();
                  clonedClient.CallerId = callerId.Value;

                  return await clonedClient.CreateAsync(entity, token);
               }

               return await _client.CreateAsync(entity, token);
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         await ExecuteWithRetryAsync(
            async (token) =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  using var clonedClient = _client.Clone();
                  clonedClient.CallerId = callerId.Value;
                  await clonedClient.UpdateAsync(entity, token);

                  return;
               }

               await _client.UpdateAsync(entity, token);
            },
            ct
         );
      }

      /// <inheritdoc />
      public async Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      )
      {
         await ExecuteWithRetryAsync(
            async (token) =>
               await _client.DeleteAsync(entityLogicalName, id, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task AssociateAsync(
         string entityLogicalName,
         Guid entityId,
         Relationship relationship,
         EntityReferenceCollection relatedEntities,
         CancellationToken ct = default
      )
      {
         await ExecuteWithRetryAsync(
            async (token) =>
               await _client.AssociateAsync(
                  entityLogicalName,
                  entityId,
                  relationship,
                  relatedEntities,
                  token
               ),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityCollection> RetrieveMultipleAsync(
         QueryBase query,
         CancellationToken ct = default
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) => await _client.RetrieveMultipleAsync(query, token),
            ct
         );
      }

      /// <inheritdoc />
      public async Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await ExecuteWithRetryAsync(
            async (token) =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  using var clonedClient = _client.Clone();
                  clonedClient.CallerId = callerId.Value;

                  return await clonedClient.ExecuteAsync(request, token);
               }

               return await _client.ExecuteAsync(request, token);
            },
            ct
         );
      }

      /// <summary>
      /// Disposes the underlying service client.
      /// </summary>
      public void Dispose()
      {
         _client.Dispose();
      }
   }
}
