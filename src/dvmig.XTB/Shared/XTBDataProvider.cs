using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;

using dvmig.Core.Interfaces;

namespace dvmig.XTB.Shared
{
   /// <summary>
   /// Implementation of <see cref="IDataverseProvider"/> that wraps an 
   /// XrmToolBox <see cref="IOrganizationService"/>.
   /// </summary>
   internal sealed class XTBDataProvider : IDataverseProvider
   {
      private readonly IOrganizationService _service;
      
      /// <inheritdoc />
      public string ConnectionString { get; }

      /// <inheritdoc />
      public bool IsLegacy { get; }

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="XTBDataProvider"/> class.
      /// </summary>
      /// <param name="service">The organization service.</param>
      /// <param name="connectionName">A  name for the connection.</param>
      /// <param name="isLegacy">
      /// Whether the environment is legacy (OnPrem).
      /// </param>
      public XTBDataProvider(
         IOrganizationService service, 
         string connectionName,
         bool isLegacy = false
      )
      {
         _service = service;
         ConnectionString = connectionName;
         IsLegacy = isLegacy;
      }

      private T ExecuteWithCallerId<T>(
         Guid? callerId, 
         Func<IOrganizationService, T> action
      )
      {
         if (!callerId.HasValue || callerId.Value == Guid.Empty)
         {
            return action(_service);
         }

         if (_service is ICloneable cloneable && 
             cloneable.Clone() is IOrganizationService clonedService)
         {
            var callerIdProp = clonedService.GetType().GetProperty("CallerId");
            if (callerIdProp != null && 
                callerIdProp.PropertyType == typeof(Guid))
            {
               callerIdProp.SetValue(clonedService, callerId.Value);
            }
            
            try
            {
               return action(clonedService);
            }
            finally
            {
               if (clonedService is IDisposable disposable)
               {
                  disposable.Dispose();
               }
            }
         }

         lock (_service)
         {
            var callerIdProp = _service.GetType().GetProperty("CallerId");
            if (callerIdProp != null && 
                callerIdProp.PropertyType == typeof(Guid))
            {
               var originalCallerId = (Guid)callerIdProp.GetValue(_service);
               callerIdProp.SetValue(_service, callerId.Value);
               
               try
               {
                  return action(_service);
               }
               finally
               {
                  callerIdProp.SetValue(_service, originalCallerId);
               }
            }
            
            return action(_service);
         }
      }

      private void ExecuteWithCallerId(
         Guid? callerId, 
         Action<IOrganizationService> action
      )
      {
         ExecuteWithCallerId<object?>(callerId, svc =>
         {
            action(svc);
            return null;
         });
      }

      /// <inheritdoc />
      public async Task<Entity?> RetrieveAsync(
         string entityLogicalName,
         Guid id,
         string[]? columns = null,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() =>
         {
            try
            {
               var columnSet = columns == null
                  ? new ColumnSet(true)
                  : new ColumnSet(columns);

               return _service.Retrieve(entityLogicalName, id, columnSet);
            }
            catch
            {
               return null;
            }
         }, ct);
      }

      /// <inheritdoc />
      public async Task<EntityMetadata?> GetEntityMetadataAsync(
         string entityLogicalName,
         CancellationToken ct = default
      )
      {
         return await Task.Run(() =>
         {
            try
            {
               var request = new RetrieveEntityRequest
               {
                  LogicalName = entityLogicalName,
                  EntityFilters = EntityFilters.Attributes
               };

               var response = (RetrieveEntityResponse)
                  _service.Execute(request);

               return response.EntityMetadata;
            }
            catch
            {
               return null;
            }
         }, ct);
      }

      /// <inheritdoc />
      public async Task<Guid> CreateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await Task.Run(() => 
            ExecuteWithCallerId(callerId, svc => svc.Create(entity)), ct);
      }

      /// <inheritdoc />
      public async Task UpdateAsync(
         Entity entity,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         await Task.Run(() => 
            ExecuteWithCallerId(callerId, svc => svc.Update(entity)), ct);
      }

      /// <inheritdoc />
      public async Task DeleteAsync(
         string entityLogicalName,
         Guid id,
         CancellationToken ct = default
      )
      {
         await Task.Run(() => _service.Delete(entityLogicalName, id), ct);
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
         await Task.Run(() => 
            _service.Associate(
               entityLogicalName, 
               entityId, 
               relationship, 
               relatedEntities
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
         return await Task.Run(() => _service.RetrieveMultiple(query), ct);
      }

      /// <inheritdoc />
      public async Task<OrganizationResponse> ExecuteAsync(
         OrganizationRequest request,
         CancellationToken ct = default,
         Guid? callerId = null
      )
      {
         return await Task.Run(() => 
            ExecuteWithCallerId(callerId, svc => svc.Execute(request)), ct);
      }
   }
}
