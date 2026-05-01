using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Serilog;

namespace dvmig.Tests
{
   public class SourceDateServiceTests
   {
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly SourceDateService _service;

      public SourceDateServiceTests()
      {
         _targetMock = new Mock<IDataverseProvider>();
         _loggerMock = new Mock<ILogger>();

         _service = new SourceDateService(_loggerMock.Object);
      }

      [Fact]
      public async Task CreateSourceDateRecordAsync_DoesNothing_WhenNotSupported()
      {
         var entity = new Entity(SystemConstants.DataverseEntities.Account, Guid.NewGuid());
         entity[SystemConstants.DataverseAttributes.CreatedOn] =
             DateTime.UtcNow;

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null);

         await _service.CreateSourceDateRecordAsync(_targetMock.Object, entity);

         _targetMock.Verify(t => t.CreateAsync(
             It.IsAny<Entity>(),
             It.IsAny<CancellationToken>()), Times.Never
         );
      }

      [Fact]
      public async Task CreateSourceDateRecordAsync_DoesNothing_WhenNoDatesPresent()
      {
         var entity = new Entity(SystemConstants.DataverseEntities.Account, Guid.NewGuid());

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         await _service.CreateSourceDateRecordAsync(_targetMock.Object, entity);

         _targetMock.Verify(t => t.CreateAsync(
             It.IsAny<Entity>(),
             It.IsAny<CancellationToken>()), Times.Never
         );
      }

      [Fact]
      public async Task CreateSourceDateRecordAsync_CreatesEntity_WhenSupported()
      {
         var entityId = Guid.NewGuid();
         var entity = new Entity(SystemConstants.DataverseEntities.Account, entityId);
         var createdOn = DateTime.UtcNow.AddDays(-1);
         var modifiedOn = DateTime.UtcNow;
         entity[SystemConstants.DataverseAttributes.CreatedOn] = createdOn;
         entity[SystemConstants.DataverseAttributes.ModifiedOn] = modifiedOn;

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         await _service.CreateSourceDateRecordAsync(_targetMock.Object, entity);

         _targetMock.Verify(t => t.CreateAsync(It.Is<Entity>(e =>
             e.LogicalName == SystemConstants.SourceDate.EntityLogicalName &&
             e[SystemConstants.SourceDate.EntityId].ToString() ==
                 entityId.ToString() &&
             e[SystemConstants.SourceDate.EntityLogicalNameAttr].ToString() ==
                 SystemConstants.DataverseEntities.Account &&
             (DateTime)e[SystemConstants.SourceDate.CreatedDate] ==
                 createdOn &&
             (DateTime)e[SystemConstants.SourceDate.ModifiedDate] ==
                 modifiedOn
         ), It.IsAny<CancellationToken>()), Times.Once);
      }

      [Fact]
      public async Task DeleteSourceDateRecordAsync_DoesNothing_WhenNotSupported()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null);

         await _service.DeleteSourceDateRecordAsync(
             _targetMock.Object,
             SystemConstants.DataverseEntities.Account,
             Guid.NewGuid()
         );

         _targetMock.Verify(t => t.RetrieveMultipleAsync(
             It.IsAny<QueryBase>(),
             It.IsAny<CancellationToken>()), Times.Never
         );
      }

      [Fact]
      public async Task DeleteSourceDateRecordAsync_DeletesRecord_WhenFound()
      {
         var entityId = Guid.NewGuid();
         var recordId = Guid.NewGuid();

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         var collection = new EntityCollection(new List<Entity>
         {
            new Entity(SystemConstants.SourceDate.EntityLogicalName, recordId)
         });

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.IsAny<QueryBase>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(collection);

         await _service.DeleteSourceDateRecordAsync(
             _targetMock.Object,
             SystemConstants.DataverseEntities.Account,
             entityId
         );

         _targetMock.Verify(t => t.DeleteAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             recordId,
             It.IsAny<CancellationToken>()), Times.Once
         );
      }

      [Fact]
      public async Task DeleteSourceDateRecordAsync_DoesNothing_WhenNotFound()
      {
         var entityId = Guid.NewGuid();

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.IsAny<QueryBase>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityCollection());

         await _service.DeleteSourceDateRecordAsync(
             _targetMock.Object,
             SystemConstants.DataverseEntities.Account,
             entityId
         );

         _targetMock.Verify(t => t.DeleteAsync(
             It.IsAny<string>(),
             It.IsAny<Guid>(),
             It.IsAny<CancellationToken>()), Times.Never
         );
      }
   }
}
