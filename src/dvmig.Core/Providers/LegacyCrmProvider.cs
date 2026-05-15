using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using Polly;
using Polly.Retry;

namespace dvmig.Core.Providers
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the legacy
   /// <see cref="CrmServiceClient"/> from the XrmTooling.Connector SDK.
   /// Required for OnPrem (AD/IFD) authentication support.
   /// </summary>
   public class LegacyCrmProvider : IDataverseProvider, IDisposable
   {
      private readonly CrmServiceClient _client;
      private readonly AsyncRetryPolicy _retryPolicy;

      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <inheritdoc />
      public bool IsLegacy => true;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="LegacyCrmProvider"/> class.
      /// </summary>
      /// <param name="connectionString">
      /// The connection string to the legacy CRM environment.
      /// </param>
      public LegacyCrmProvider(string connectionString)
      {
         ConnectionString = connectionString;
         _client = new CrmServiceClient(connectionString);

         if (!_client.IsReady)
            throw new Exception(
               $"Legacy CRM connection failed: {_client.LastCrmError}"
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
            async (token) => await Task.Run(() =>
            {
               try
               {
                  var columnSet = columns == null
                     ? new ColumnSet(true)
                     : new ColumnSet(columns);

                  return _client.Retrieve(
                     entityLogicalName,
                     id,
                     columnSet
                  );
               }
               catch (Exception ex)
               {
                  if (ex.IsNotFoundException())
                     return null;

                  throw;
               }
            }, token),
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
            async (token) => await Task.Run(() =>
            {
               var response = _client.Execute(
                  new RetrieveEntityRequest
                  {
                     LogicalName = entityLogicalName,
                     EntityFilters = EntityFilters.Attributes
                  }
               ) as RetrieveEntityResponse;

               return response?.EntityMetadata;
            }, token),
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
            async (token) => await Task.Run(() =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  using var clonedClient = _client.Clone();
                  clonedClient.CallerId = callerId.Value;

                  return clonedClient.Create(entity);
               }

               return _client.Create(entity);
            }, token),
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
            async (token) => await Task.Run(() =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  using var clonedClient = _client.Clone();
                  clonedClient.CallerId = callerId.Value;
                  clonedClient.Update(entity);

                  return;
               }

               _client.Update(entity);
            }, token),
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
            async (token) => await Task.Run(
               () => _client.Delete(entityLogicalName, id),
               token
            ),
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
            async (token) => await Task.Run(() => _client.Associate(
               entityLogicalName,
               entityId,
               relationship,
               relatedEntities
            ), token),
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
            async (token) => await Task.Run(
               () => _client.RetrieveMultiple(query),
               token
            ),
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
            async (token) => await Task.Run(() =>
            {
               if (callerId.HasValue && callerId.Value != Guid.Empty)
               {
                  using var clonedClient = _client.Clone();
                  clonedClient.CallerId = callerId.Value;

                  return clonedClient.Execute(request);
               }

               return _client.Execute(request);
            }, token),
            ct
         );
      }

      /// <summary>
      /// Disposes the underlying CRM service client.
      /// </summary>
      public void Dispose()
      {
         _client.Dispose();
      }
   }
}
