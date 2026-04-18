using dvmig.Core;
using dvmig.Providers;
using Microsoft.Xrm.Sdk;
using Moq;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace dvmig.Tests
{
    public class SyncEngineTests
    {
        private readonly Mock<IDataverseProvider> _sourceMock;
        private readonly Mock<IDataverseProvider> _targetMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly SyncEngine _engine;

        public SyncEngineTests()
        {
            _sourceMock = new Mock<IDataverseProvider>();
            _targetMock = new Mock<IDataverseProvider>();
            _loggerMock = new Mock<ILogger>();
            _engine = new SyncEngine(
                _sourceMock.Object, 
                _targetMock.Object, 
                _loggerMock.Object
            );
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

            // First attempt to create contact fails with "account does not exist"
            _targetMock.SetupSequence(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "contact"), 
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("account with Id=" + accountId + " does not exist"))
                .ReturnsAsync(contactId); // Succeeds on second attempt

            // Setup source to return the missing account
            _sourceMock.Setup(s => s.RetrieveAsync("account", accountId, 
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(account);

            // Setup target to succeed on account creation
            _targetMock.Setup(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(accountId);

            var options = new SyncOptions { SkipExisting = false };

            // Act
            var result = await _engine.SyncRecordAsync(contact, options);

            // Assert
            Assert.True(result);
            _targetMock.Verify(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "account"), 
                It.IsAny<CancellationToken>()), Times.Once);
            _targetMock.Verify(t => t.CreateAsync(
                It.Is<Entity>(e => e.LogicalName == "contact"), 
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
