using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Serilog;

namespace dvmig.Tests
{
    public class DataPreservationManagerTests
    {
        private readonly Mock<IDataverseProvider> _targetMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly SetupService _service;

        public DataPreservationManagerTests()
        {
            _targetMock = new Mock<IDataverseProvider>();
            _loggerMock = new Mock<ILogger>();

            var validator = new Mock<IEnvironmentValidator>();
            var schemaManager = new Mock<ISchemaManager>();
            var pluginDeployer = new Mock<IPluginDeployer>();

            _service = new SetupService(
                validator.Object,
                schemaManager.Object,
                pluginDeployer.Object,
                _loggerMock.Object
            );
        }

        [Fact]
        public async Task PreserveDatesAsync_DoesNothing_WhenNotSupported()
        {
            var entity = new Entity("account", Guid.NewGuid());
            entity["createdon"] = DateTime.UtcNow;

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                It.IsAny<CancellationToken>())
            ).ReturnsAsync((EntityMetadata?)null);

            await _service.PreserveDatesAsync(_targetMock.Object, entity);

            _targetMock.Verify(t => t.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()), Times.Never
            );
        }

        [Fact]
        public async Task PreserveDatesAsync_DoesNothing_WhenNoDatesPresent()
        {
            var entity = new Entity("account", Guid.NewGuid());

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            await _service.PreserveDatesAsync(_targetMock.Object, entity);

            _targetMock.Verify(t => t.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()), Times.Never
            );
        }

        [Fact]
        public async Task PreserveDatesAsync_CreatesSourceDateEntity_WhenSupportedAndDatesPresent()
        {
            var entityId = Guid.NewGuid();
            var entity = new Entity("account", entityId);
            var createdOn = DateTime.UtcNow.AddDays(-1);
            var modifiedOn = DateTime.UtcNow;
            entity["createdon"] = createdOn;
            entity["modifiedon"] = modifiedOn;

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            await _service.PreserveDatesAsync(_targetMock.Object, entity);

            _targetMock.Verify(t => t.CreateAsync(It.Is<Entity>(e =>
                e.LogicalName == SystemConstants.SourceDate.EntityLogicalName &&
                e[SystemConstants.SourceDate.EntityId].ToString() == entityId.ToString() &&
                e[SystemConstants.SourceDate.EntityLogicalNameAttr].ToString() == "account" &&
                (DateTime)e[SystemConstants.SourceDate.CreatedDate] == createdOn &&
                (DateTime)e[SystemConstants.SourceDate.ModifiedDate] == modifiedOn
            ), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteSourceDateAsync_DoesNothing_WhenNotSupported()
        {
            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                It.IsAny<CancellationToken>())
            ).ReturnsAsync((EntityMetadata?)null);

            await _service.DeleteSourceDateAsync(_targetMock.Object, "account", Guid.NewGuid());

            _targetMock.Verify(t => t.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()), Times.Never
            );
        }

        [Fact]
        public async Task DeleteSourceDateAsync_DeletesRecord_WhenFound()
        {
            var entityId = Guid.NewGuid();
            var sourceDateId = Guid.NewGuid();

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            var fetchResult = new EntityCollection(
                new[] { new Entity(SystemConstants.SourceDate.EntityLogicalName, sourceDateId) }
            );
            _targetMock.Setup(t => t.RetrieveMultipleAsync(
                It.IsAny<FetchExpression>(),
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(fetchResult);

            await _service.DeleteSourceDateAsync(_targetMock.Object, "account", entityId);

            _targetMock.Verify(t => t.DeleteAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                sourceDateId,
                It.IsAny<CancellationToken>()), Times.Once
            );
        }

        [Fact]
        public async Task DeleteSourceDateAsync_DoesNothing_WhenNotFound()
        {
            var entityId = Guid.NewGuid();

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                SystemConstants.SourceDate.EntityLogicalName,
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            var fetchResult = new EntityCollection();
            _targetMock.Setup(t => t.RetrieveMultipleAsync(
                It.IsAny<FetchExpression>(),
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(fetchResult);

            await _service.DeleteSourceDateAsync(_targetMock.Object, "account", entityId);

            _targetMock.Verify(t => t.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()), Times.Never
            );
        }
    }
}
