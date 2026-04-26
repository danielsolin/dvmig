using dvmig.Core.DataPreservation;
using dvmig.Core.Synchronization;
using dvmig.Providers;
using dvmig.Shared.Metadata;
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
        private readonly Mock<ISyncStateTracker> _stateTrackerMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly SyncEngine _engine;

        public SyncEngineTests()
        {
            _sourceMock = new Mock<IDataverseProvider>();
            _targetMock = new Mock<IDataverseProvider>();
            _userMapperMock = new Mock<IUserMapper>();
            _dataPreservationMock = new Mock<IDataPreservationManager>();
            _stateTrackerMock = new Mock<ISyncStateTracker>();
            _loggerMock = new Mock<ILogger>();

            _stateTrackerMock.Setup(s => s.GetSyncedIdsAsync())
                .ReturnsAsync(new HashSet<Guid>());

            _engine = new SyncEngine(
                _sourceMock.Object,
                _targetMock.Object,
                _userMapperMock.Object,
                _dataPreservationMock.Object,
                _stateTrackerMock.Object,
                _loggerMock.Object
            );

            _userMapperMock.Setup(m => m.MapUserAsync(
                It.IsAny<EntityReference>(),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync((EntityReference r, CancellationToken ct) => r);
        }

        [Fact]
        public async Task SyncRecordAsync_StripReadOnly_OnForbidden()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var account = new Entity("account", accountId);
            account["name"] = "Test Account";
            account["readonlyfield"] = "Value";

            int callCount = 0;
            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"),
                It.IsAny<CancellationToken>()
            ))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    callCount++;
                    if (e.Attributes.Contains("readonlyfield"))
                    {
                        throw new Exception(
                            "The property 'readonlyfield' cannot be modified."
                        );
                    }

                    return Task.FromResult(accountId);
                });

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var (result, _) = await _engine.SyncRecordAsync(account, options);

            // Assert
            Assert.True(result);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task SyncRecordAsync_SyncDependency_WhenMissing()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var contactId = Guid.NewGuid();

            var contact = new Entity("contact", contactId);
            contact["parentcustomerid"] =
                new EntityReference("account", accountId);

            var account = new Entity("account", accountId);
            account["name"] = "Test Account";

            int contactCreateCalls = 0;
            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "contact"),
                It.IsAny<CancellationToken>()
            ))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    contactCreateCalls++;
                    if (contactCreateCalls == 1)
                    {
                        throw new Exception(
                            "account with Id=" + accountId + " does not exist"
                        );
                    }

                    return Task.FromResult(contactId);
                });

            _sourceMock.Setup(s => s.RetrieveAsync(
                "account",
                accountId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(account);

            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(accountId);

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var (result, _) = await _engine.SyncRecordAsync(contact, options);

            // Assert
            Assert.True(result);
            Assert.Equal(2, contactCreateCalls);
            _targetMock.Verify(
                t => t.CreateAsync(
                    It.Is<Entity>(e => e.LogicalName == "account"),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task SyncRecordAsync_CallAssociate_WhenEntityIsIntersect()
        {
            // Arrange
            var relName = "new_account_contact";
            var accountId = Guid.NewGuid();
            var contactId = Guid.NewGuid();

            var intersectEntity = new Entity(relName, Guid.NewGuid());
            intersectEntity["accountid"] =
                new EntityReference("account", accountId);
            intersectEntity["contactid"] =
                new EntityReference("contact", contactId);

            var metadata = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata
            {
                LogicalName = relName
            };

            typeof(Microsoft.Xrm.Sdk.Metadata.EntityMetadata)
                .GetProperty(nameof(metadata.IsIntersect))
                ?.SetValue(metadata, true);

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                relName,
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(metadata);

            _targetMock.Setup(t => t.ExecuteAsync(
                It.IsAny<AssociateRequest>(),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(new AssociateResponse());

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var (result, _) = await _engine.SyncRecordAsync(
                intersectEntity,
                options
            );

            // Assert
            Assert.True(result);
            _targetMock.Verify(
                t => t.ExecuteAsync(
                    It.IsAny<AssociateRequest>(),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task SyncRecordAsync_MapUser_WhenAttributeIsUserField()
        {
            // Arrange
            var sourceUserId = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();
            var sourceUserRef = new EntityReference("systemuser", sourceUserId);
            var targetUserRef = new EntityReference("systemuser", targetUserId);

            var account = new Entity("account", Guid.NewGuid());
            account["ownerid"] = sourceUserRef;

            _userMapperMock.Setup(m => m.MapUserAsync(
                sourceUserRef,
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(targetUserRef);

            _targetMock.Setup(t => t.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(account.Id);

            var options = new SyncOptions { SkipExisting = false };

            // Act
            await _engine.SyncRecordAsync(account, options);

            // Assert
            _targetMock.Verify(
                t => t.CreateAsync(
                    It.Is<Entity>(e =>
                        ((EntityReference)e["ownerid"]).Id == targetUserId),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task SyncRecordAsync_PreserveDates_WhenOptionIsEnabled()
        {
            // Arrange
            var account = new Entity("account", Guid.NewGuid());
            account["name"] = "Date Test";
            account["createdon"] = DateTime.UtcNow;

            _targetMock.Setup(t => t.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(account.Id);

            var options = new SyncOptions
            {
                SkipExisting = false,
                PreserveDates = true
            };

            // Act
            await _engine.SyncRecordAsync(account, options);

            // Assert
            _dataPreservationMock.Verify(
                p => p.PreserveDatesAsync(
                    account,
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task SyncRecordAsync_UpdateExisting_WhenCreateFailsWithDuplicate()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var account = new Entity("account", accountId)
            {
                ["name"] = "Existing Account",
                ["telephone1"] = "12345"
            };

            int createCalls = 0;
            _targetMock.Setup(t => t.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()
            ))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    createCalls++;

                    throw new Exception("A record with this ID already exists.");
                });

            _targetMock.Setup(t => t.UpdateAsync(
                It.Is<Entity>(e => e.Id == accountId),
                It.IsAny<CancellationToken>()
            ))
                .Returns(Task.CompletedTask);

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var (result, _) = await _engine.SyncRecordAsync(account, options);

            // Assert
            Assert.True(result);
            Assert.Equal(1, createCalls);
            _targetMock.Verify(
                t => t.UpdateAsync(
                    It.Is<Entity>(e => (string)e["telephone1"] == "12345"),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task SyncRecordAsync_Retry_OnServiceProtectionLimit()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var account = new Entity("account", accountId)
            {
                ["name"] = "Retry Test"
            };

            int callCount = 0;
            _targetMock.Setup(t => t.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()
            ))
                .Returns<Entity, CancellationToken>((e, ct) =>
                {
                    callCount++;

                    // Simulate Service Protection Limit error
                    if (callCount == 1)
                    {
                        throw new Exception(
                            "Rate limit exceeded. Error Code: 0x8004410d"
                        );
                    }

                    return Task.FromResult(accountId);
                });

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var (result, _) = await _engine.SyncRecordAsync(account, options);

            // Assert
            Assert.True(result);
            Assert.Equal(2, callCount); // Verified that it retried
        }

        [Fact]
        public async Task SyncAsync_RegistersFailureRecord_WhenCreateFails()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var account = new Entity("account", accountId)
            {
                ["name"] = "Failure Account"
            };

            var metadata = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata
            {
                LogicalName = "account"
            };

            _targetMock.Setup(t => t.GetEntityMetadataAsync(
                "account",
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(metadata);

            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"),
                It.IsAny<CancellationToken>()
            ))
                .ThrowsAsync(new Exception("Create failed"));

            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == SchemaConstants.MigrationFailure.EntityLogicalName),
                It.IsAny<CancellationToken>()
            ))
                .ReturnsAsync(Guid.NewGuid());

            var options = new SyncOptions { SkipExisting = false };

            // Act
            await _engine.SyncAsync(
                new[] { account },
                options
            );

            // Assert
            _targetMock.Verify(t => t.CreateAsync(
                It.Is<Entity>(e =>
                    e.LogicalName == SchemaConstants.MigrationFailure.EntityLogicalName
                ),
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }
    }
}
