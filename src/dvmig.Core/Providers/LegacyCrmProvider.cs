using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the legacy
   /// <see cref="CrmServiceClient"/> from the XrmTooling.Connector SDK.
   /// Required for OnPrem (AD/IFD) authentication support.
   /// </summary>
   public class LegacyCrmProvider : IDataverseProvider, IDisposable
   {
      private readonly CrmServiceClient _client;

      /// <inheritdoc />
      public string ConnectionString { get; }

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
      }

      /// <inheritdoc />
      public Guid? CallerId
      {
         get
         {
            return _client.CallerId;
         }
         set
         {
            _client.CallerId = value ?? Guid.Empty;
         }
      }

      /// <inheritdoc />
      public Task<Entity?> RetrieveAsync(
          string entityLogicalName,
          Guid id,
          string[]? columns = null,
          CancellationToken ct = default)
      {
         try
         {
            var columnSet = columns == null
                ? new ColumnSet(true)
                : new ColumnSet(columns);

            return Task.FromResult<Entity?>(
                _client.Retrieve(
                    entityLogicalName,
                    id,
                    columnSet
                )
            );
         }
         catch (FaultException ex)
         {
            if (ex.Message.Contains("80040217") ||
                ex.Message.Contains("Does Not Exist"))
               return Task.FromResult<Entity?>(null);

            throw;
         }
      }

      /// <inheritdoc />
      public Task<EntityMetadata?> GetEntityMetadataAsync(
          string entityLogicalName,
          CancellationToken ct = default)
      {
         var response = _client.Execute(
             new RetrieveEntityRequest
             {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Attributes
             }
         ) as RetrieveEntityResponse;

         return Task.FromResult(response?.EntityMetadata);
      }

      /// <inheritdoc />
      public Task<Guid> CreateAsync(
          Entity entity,
          CancellationToken ct = default)
      {
         return Task.FromResult(_client.Create(entity));
      }

      /// <inheritdoc />
      public Task UpdateAsync(
          Entity entity,
          CancellationToken ct = default)
      {
         _client.Update(entity);

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task DeleteAsync(
          string entityLogicalName,
          Guid id,
          CancellationToken ct = default)
      {
         _client.Delete(entityLogicalName, id);

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task AssociateAsync(
          string entityLogicalName,
          Guid entityId,
          Relationship relationship,
          EntityReferenceCollection relatedEntities,
          CancellationToken ct = default)
      {
         _client.Associate(
             entityLogicalName,
             entityId,
             relationship,
             relatedEntities
         );

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task<EntityCollection> RetrieveMultipleAsync(
          QueryBase query,
          CancellationToken ct = default)
      {
         return Task.FromResult(_client.RetrieveMultiple(query));
      }

      /// <inheritdoc />
      public Task<OrganizationResponse> ExecuteAsync(
          OrganizationRequest request,
          CancellationToken ct = default)
      {
         return Task.FromResult(_client.Execute(request));
      }

      /// <inheritdoc />
      public async Task<long> GetRecordCountAsync(
          string entityLogicalName,
          CancellationToken ct = default)
      {
         var metadata = await GetEntityMetadataAsync(entityLogicalName, ct);
         var primaryId = metadata?.PrimaryIdAttribute ?? 
                         $"{entityLogicalName}id";

         var fetchXml = $@"
            <fetch aggregate='true'>
              <entity name='{entityLogicalName}'>
                <attribute name='{primaryId}' alias='count' aggregate='count' />
              </entity>
            </fetch>";

         var result = _client.RetrieveMultiple(new FetchExpression(fetchXml));

         if (result.Entities.Count > 0 &&
             result.Entities[0].Contains("count"))
         {
            var aliasedValue = (AliasedValue)result.Entities[0]["count"];

            return Convert.ToInt64(aliasedValue.Value);
         }

         return 0;
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
