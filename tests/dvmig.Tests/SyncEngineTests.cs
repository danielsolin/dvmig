using dvmig.Core;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Serilog;

namespace dvmig.Tests
{
    public class SyncEngineTests
    {
        private readonly Mock<IDataverseProvider> _sourceMock;
        private readonly Mock<IDataverseProvider> _targetMock;
        private readonly Mock<IUserMapper> _userMapperMock;
        private readonly Mock<IDataPreservationManager> _dataPreservationMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly SyncEngine _engine;

        public SyncEngineTests()
        {
            _sourceMock = new Mock<IDataverseProvider>();
            _targetMock = new Mock<IDataverseProvider>();
            _userMapperMock = new Mock<IUserMapper>();
            _dataPreservationMock = new Mock<IDataPreservationManager>();
            _loggerMock = new Mock<ILogger>();
            
            _engine = new SyncEngine(
                _sourceMock.Object, 
                _targetMock.Object, 
                _userMapperMock.Object,
                _dataPreservationMock.Object,
                _loggerMock.Object
            );

            _userMapperMock.Setup(m => m.MapUserAsync(It.IsAny<EntityReference>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((EntityReference r, CancellationToken ct) => r);
        }

        [Fact]
        public async Task SyncRecordAsync_ShouldStripReadOnlyAttribute_WhenModificationForbidden()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var account = new Entity("account", accountId);
            account["name"] = "Test Account";
            account["readonlyfield"] = "Value";

            int callCount = 0;
            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"), 
                It.IsAny<CancellationToken>()))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    callCount++;
                    if (e.Attributes.Contains("readonlyfield"))
                    {
                        throw new Exception("The property 'readonlyfield' cannot be modified.");
                    }
                    
                    return Task.FromResult(accountId);
                });

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var result = await _engine.SyncRecordAsync(account, options);

            // Assert
            Assert.True(result);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task SyncRecordAsync_ShouldRecursivelySyncDependency_WhenMissing()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var contactId = Guid.NewGuid();

            var contact = new Entity("contact", contactId);
            contact["parentcustomerid"] = new EntityReference("account", accountId);

            var account = new Entity("account", accountId);
            account["name"] = "Test Account";

            int contactCreateCalls = 0;
            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "contact"), 
                It.IsAny<CancellationToken>()))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    contactCreateCalls++;
                    if (contactCreateCalls == 1)
                    {
                        throw new Exception("account with Id=" + accountId + " does not exist");
                    }
                    
                    return Task.FromResult(contactId);
                });

            _sourceMock.Setup(s => s.RetrieveAsync("account", accountId, 
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);

            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(accountId);

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var result = await _engine.SyncRecordAsync(contact, options);

            // Assert
            Assert.True(result);
            Assert.Equal(2, contactCreateCalls);
            _targetMock.Verify(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"), 
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SyncBulkAsync_ShouldShuntToSingleSync_WhenBulkItemFails()
        {
            // Arrange
            var entity1 = new Entity("account", Guid.NewGuid()) { ["name"] = "Success" };
            var entity2 = new Entity("account", Guid.NewGuid()) { ["name"] = "Fail" };
            var entities = new List<Entity> { entity1, entity2 };

            var metadata = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata { LogicalName = "account" };
            _targetMock.Setup(t => t.GetEntityMetadataAsync("account", It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata);

            var bulkResponse = new ExecuteMultipleResponse();
            bulkResponse.Results["Responses"] = new ExecuteMultipleResponseItemCollection
            {
                new ExecuteMultipleResponseItem { RequestIndex = 0, Response = new CreateResponse() },
                new ExecuteMultipleResponseItem { RequestIndex = 1, Fault = new OrganizationServiceFault { Message = "Bulk Error" } }
            };

            _targetMock.Setup(t => t.ExecuteAsync(It.IsAny<ExecuteMultipleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(bulkResponse);

            _targetMock.Setup(t => t.CreateAsync(It.Is<Entity>(e => (string)e["name"] == "Fail"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity2.Id);

            var options = new SyncOptions { UseBulk = true, BulkBatchSize = 10, SkipExisting = false };

            // Act
            await _engine.SyncAsync(entities, options);

            // Assert
            _targetMock.Verify(t => t.ExecuteAsync(It.IsAny<ExecuteMultipleRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _targetMock.Verify(t => t.CreateAsync(It.Is<Entity>(e => (string)e["name"] == "Fail"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SyncRecordAsync_ShouldCallAssociate_WhenEntityIsIntersect()
        {
            // Arrange
            var relName = "new_account_contact";
            var accountId = Guid.NewGuid();
            var contactId = Guid.NewGuid();

            var intersectEntity = new Entity(relName, Guid.NewGuid());
            intersectEntity["accountid"] = new EntityReference("account", accountId);
            intersectEntity["contactid"] = new EntityReference("contact", contactId);

            var metadata = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata { LogicalName = relName };
            typeof(Microsoft.Xrm.Sdk.Metadata.EntityMetadata)
                .GetProperty(nameof(metadata.IsIntersect))
                ?.SetValue(metadata, true);

            _targetMock.Setup(t => t.GetEntityMetadataAsync(relName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata);

            _targetMock.Setup(t => t.ExecuteAsync(It.IsAny<AssociateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AssociateResponse());

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var result = await _engine.SyncRecordAsync(intersectEntity, options);

            // Assert
            Assert.True(result);
            _targetMock.Verify(t => t.ExecuteAsync(It.IsAny<AssociateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SyncRecordAsync_ShouldMapUser_WhenAttributeIsUserField()
        {
            // Arrange
            var sourceUserId = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            var sourceUserRef = new EntityReference("systemuser", sourceUserId);
            var targetUserRef = new EntityReference("systemuser", targetUserId);

            var account = new Entity("account", Guid.NewGuid());
            account["ownerid"] = sourceUserRef;

            _userMapperMock.Setup(m => m.MapUserAsync(sourceUserRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(targetUserRef);

            _targetMock.Setup(t => t.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(account.Id);

            var options = new SyncOptions { SkipExisting = false };

            // Act
            await _engine.SyncRecordAsync(account, options);

            // Assert
            _targetMock.Verify(t => t.CreateAsync(It.Is<Entity>(e => 
                ((EntityReference)e["ownerid"]).Id == targetUserId), 
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SyncRecordAsync_ShouldPreserveDates_WhenOptionIsEnabled()
        {
            // Arrange
            var account = new Entity("account", Guid.NewGuid());
            account["name"] = "Date Test";
            account["createdon"] = DateTime.UtcNow;

            _targetMock.Setup(t => t.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(account.Id);

            var options = new SyncOptions { SkipExisting = false, PreserveDates = true };

            // Act
            await _engine.SyncRecordAsync(account, options);

            // Assert
            _dataPreservationMock.Verify(p => p.PreserveDatesAsync(account, 
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SyncRecordAsync_ShouldRetry_WhenServiceProtectionLimitReached()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var account = new Entity("account", accountId) { ["name"] = "Retry Test" };

            int callCount = 0;
            _targetMock.Setup(t => t.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // Simulate Service Protection Limit error
                        throw new Exception("Rate limit exceeded. Error Code: 0x8004410d");
                    }
                    
                    return Task.FromResult(accountId);
                });

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var result = await _engine.SyncRecordAsync(account, options);

            // Assert
            Assert.True(result);
            Assert.Equal(2, callCount); // Verified that it retried
        }
    }
}
