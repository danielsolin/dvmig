using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace dvmig.Core.Interfaces
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> using the modern
   /// <see cref="ServiceClient"/> from the PowerPlatform.Dataverse.Client SDK.
   /// </summary>
   public class DataverseProvider : IDataverseProvider, IDisposable
   {
      private readonly ServiceClient _client;

      /// <inheritdoc />
      public string ConnectionString { get; }

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
      public async Task<Entity?> RetrieveAsync(
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

            return await _client.RetrieveAsync(
                entityLogicalName,
                id,
                columnSet,
                ct
            );
         }
         catch (FaultException ex)
         {
            // 0x80040217 = Object does not exist
            if (ex.Message.Contains("80040217") ||
                ex.Message.Contains("Does Not Exist"))
               return null;

            throw;
         }
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetEntityMetadataAsync(
          string entityLogicalName,
          CancellationToken ct = default)
      {
         try
         {
            var response = await _client.ExecuteAsync(
                new RetrieveEntityRequest
                {
                   LogicalName = entityLogicalName,
                   EntityFilters = EntityFilters.Attributes
                },
                ct
            ) as RetrieveEntityResponse;

            return response?.EntityMetadata;
         }
         catch
         {
            // If entity doesn't exist, RetrieveEntityRequest throws.
            // We return null to indicate missing metadata.

            return null;
         }
      }

      /// <inheritdoc />
      public async Task<Guid> CreateAsync(
          Entity entity,
          CancellationToken ct = default)
      {
         return await _client.CreateAsync(entity, ct);
      }

      /// <inheritdoc />
      public async Task UpdateAsync(
          Entity entity,
          CancellationToken ct = default)
      {
         await _client.UpdateAsync(entity, ct);
      }

      /// <inheritdoc />
      public async Task DeleteAsync(
          string entityLogicalName,
          Guid id,
          CancellationToken ct = default)
      {
         await _client.DeleteAsync(entityLogicalName, id, ct);
      }

      /// <inheritdoc />
      public async Task AssociateAsync(
          string entityLogicalName,
          Guid entityId,
          Relationship relationship,
          EntityReferenceCollection relatedEntities,
          CancellationToken ct = default)
      {
         await _client.AssociateAsync(
             entityLogicalName,
             entityId,
             relationship,
             relatedEntities,
             ct
         );
      }

      /// <inheritdoc />
      public async Task<EntityCollection> RetrieveMultipleAsync(
          QueryBase query,
          CancellationToken ct = default)
      {
         return await _client.RetrieveMultipleAsync(query, ct);
      }

      /// <inheritdoc />
      public async Task<OrganizationResponse> ExecuteAsync(
          OrganizationRequest request,
          CancellationToken ct = default)
      {
         return await _client.ExecuteAsync(request, ct);
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

         var result = await _client.RetrieveMultipleAsync(
             new FetchExpression(fetchXml),
             ct
         );

         if (result.Entities.Count > 0 &&
             result.Entities[0].Contains("count"))
         {
            var aliasedValue = (AliasedValue)result.Entities[0]["count"];

            return Convert.ToInt64(aliasedValue.Value);
         }

         return 0;
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
