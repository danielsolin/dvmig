using dvmig.Core.DataPreservation;
using dvmig.Providers;
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
        private readonly DataPreservationManager _manager;

        public DataPreservationManagerTests()
        {
            _targetMock = new Mock<IDataverseProvider>();
            _loggerMock = new Mock<ILogger>();
            _manager = new DataPreservationManager(
                _targetMock.Object,
                _loggerMock.Object
            );
        }

        [Fact]
        public async Task PreserveDatesAsync_DoesNothing_WhenNotSupported()
        {
            var entity = new Entity("account", Guid.NewGuid());
            entity["createdon"] = DateTime.UtcNow;

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                "dm_sourcedate",
                It.IsAny<CancellationToken>())
            ).ReturnsAsync((EntityMetadata?)null);

            await _manager.PreserveDatesAsync(entity);

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
                "dm_sourcedate",
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            await _manager.PreserveDatesAsync(entity);

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
                "dm_sourcedate",
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            await _manager.PreserveDatesAsync(entity);

            _targetMock.Verify(t => t.CreateAsync(It.Is<Entity>(e =>
                e.LogicalName == "dm_sourcedate" &&
                e["dm_sourceentityid"].ToString() == entityId.ToString() &&
                e["dm_sourceentitylogicalname"].ToString() == "account" &&
                (DateTime)e["dm_sourcecreateddate"] == createdOn &&
                (DateTime)e["dm_sourcemodifieddate"] == modifiedOn
            ), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteSourceDateAsync_DoesNothing_WhenNotSupported()
        {
            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                "dm_sourcedate",
                It.IsAny<CancellationToken>())
            ).ReturnsAsync((EntityMetadata?)null);

            await _manager.DeleteSourceDateAsync("account", Guid.NewGuid());

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
                "dm_sourcedate",
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            var fetchResult = new EntityCollection(
                new[] { new Entity("dm_sourcedate", sourceDateId) }
            );
            _targetMock.Setup(t => t.RetrieveMultipleAsync(
                It.IsAny<FetchExpression>(),
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(fetchResult);

            await _manager.DeleteSourceDateAsync("account", entityId);

            _targetMock.Verify(t => t.DeleteAsync(
                "dm_sourcedate",
                sourceDateId,
                It.IsAny<CancellationToken>()), Times.Once
            );
        }

        [Fact]
        public async Task DeleteSourceDateAsync_DoesNothing_WhenNotFound()
        {
            var entityId = Guid.NewGuid();

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                "dm_sourcedate",
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(new EntityMetadata());

            var fetchResult = new EntityCollection();
            _targetMock.Setup(t => t.RetrieveMultipleAsync(
                It.IsAny<FetchExpression>(),
                It.IsAny<CancellationToken>())
            ).ReturnsAsync(fetchResult);

            await _manager.DeleteSourceDateAsync("account", entityId);

            _targetMock.Verify(t => t.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()), Times.Never
            );
        }
    }
}
