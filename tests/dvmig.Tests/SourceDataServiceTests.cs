using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;

namespace dvmig.Tests
{
   public class SourceDataServiceTests
   {
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<IUserResolver> _userResolverMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly SourceDataService _service;

      public SourceDataServiceTests()
      {
         _targetMock = new Mock<IDataverseProvider>();
         _userResolverMock = new Mock<IUserResolver>();
         _loggerMock = new Mock<ILogger>();

         _service = new SourceDataService(_loggerMock.Object);
      }

      [Fact]
      public async Task CreateSourceDataRecordAsync_DoesNothing_WhenNotSupported()
      {
         var entity = new Entity(
            SystemConstants.DataverseEntities.Account,
            Guid.NewGuid()
         );

         entity[SystemConstants.DataverseAttributes.CreatedOn] =
            DateTime.UtcNow;

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((EntityMetadata?)null);

         await _service.CreateSourceDataRecordAsync(
            _targetMock.Object,
            entity,
            _userResolverMock.Object
         );

         _targetMock.Verify(
            t => t.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>()
            ),
            Times.Never
         );
      }

      [Fact]
      public async Task CreateSourceDataRecordAsync_DoesNothing_WhenNoAuditDataPresent()
      {
         var entity = new Entity(
            SystemConstants.DataverseEntities.Account,
            Guid.NewGuid()
         );

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         await _service.CreateSourceDataRecordAsync(
            _targetMock.Object,
            entity,
            _userResolverMock.Object
         );

         _targetMock.Verify(
            t => t.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>()
            ),
            Times.Never
         );
      }

      [Fact]
      public async Task CreateSourceDataRecordAsync_CreatesEntity_WhenSupported()
      {
         var entityId = Guid.NewGuid();

         var entity = new Entity(
            SystemConstants.DataverseEntities.Account,
            entityId
         );

         var createdOn = DateTime.UtcNow.AddDays(-1);
         var modifiedOn = DateTime.UtcNow;

         entity[SystemConstants.DataverseAttributes.CreatedOn] = createdOn;
         entity[SystemConstants.DataverseAttributes.ModifiedOn] = modifiedOn;

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         await _service.CreateSourceDataRecordAsync(
            _targetMock.Object,
            entity,
            _userResolverMock.Object
         );

         _targetMock.Verify(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        SystemConstants.SourceData.EntityLogicalName &&
                     e[SystemConstants.SourceData.EntityId].ToString() ==
                        entityId.ToString() &&
                     e[SystemConstants.SourceData.EntityLogicalNameAttr]
                        .ToString() ==
                        SystemConstants.DataverseEntities.Account &&
                     (DateTime)e[SystemConstants.SourceData.CreatedOn] ==
                        createdOn &&
                     (DateTime)e[SystemConstants.SourceData.ModifiedOn] ==
                        modifiedOn
               ),
               It.IsAny<CancellationToken>()
            ),
            Times.Once
         );
      }

      [Fact]
      public async Task DeleteSourceDataRecordAsync_DoesNothing_WhenNotSupported()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((EntityMetadata?)null);

         await _service.DeleteSourceDataRecordAsync(
            _targetMock.Object,
            SystemConstants.DataverseEntities.Account,
            Guid.NewGuid()
         );

         _targetMock.Verify(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryBase>(),
               It.IsAny<CancellationToken>()
            ),
            Times.Never
         );
      }

      [Fact]
      public async Task DeleteSourceDataRecordAsync_DeletesRecord_WhenFound()
      {
         var entityId = Guid.NewGuid();
         var recordId = Guid.NewGuid();

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         var collection = new EntityCollection(
            new List<Entity>
            {
               new Entity(
                  SystemConstants.SourceData.EntityLogicalName,
                  recordId
               )
            }
         );

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryBase>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(collection);

         await _service.DeleteSourceDataRecordAsync(
            _targetMock.Object,
            SystemConstants.DataverseEntities.Account,
            entityId
         );

         _targetMock.Verify(
            t => t.DeleteAsync(
               SystemConstants.SourceData.EntityLogicalName,
               recordId,
               It.IsAny<CancellationToken>()
            ),
            Times.Once
         );
      }

      [Fact]
      public async Task DeleteSourceDataRecordAsync_DoesNothing_WhenNotFound()
      {
         var entityId = Guid.NewGuid();

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceData.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryBase>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         await _service.DeleteSourceDataRecordAsync(
            _targetMock.Object,
            SystemConstants.DataverseEntities.Account,
            entityId
         );

         _targetMock.Verify(
            t => t.DeleteAsync(
               It.IsAny<string>(),
               It.IsAny<Guid>(),
               It.IsAny<CancellationToken>()
            ),
            Times.Never
         );
      }
   }
}
