using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Polly;

namespace dvmig.Tests
{
   public class ProvisioningTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<IRetryService> _retryServiceMock;
      private readonly PluginService _pluginService;
      private readonly SeedingService _seedingService;

      public ProvisioningTests()
      {
         _loggerMock = new Mock<ILogger>();
         _targetMock = new Mock<IDataverseProvider>();
         _retryServiceMock = new Mock<IRetryService>();
         _pluginService = new PluginService(_loggerMock.Object);

         _seedingService = new SeedingService(
            _loggerMock.Object,
            _retryServiceMock.Object
         );
      }

      [Fact]
      public async Task DeployPluginAsync_ThrowsFileNotFound_WhenDllNotFound()
      {
         await Assert.ThrowsAsync<FileNotFoundException>(
            () =>
               _pluginService.DeployPluginAsync(
                  _targetMock.Object,
                  "non_existent_path.dll"
               )
         );
      }

      [Fact]
      public async Task SeedSampleDataAsync_CreatesRecords()
      {
         // Arrange
         var providerMock = new Mock<IDataverseProvider>();
         var retryPolicy = Policy.Handle<Exception>().RetryAsync(0);
         _retryServiceMock.Setup(r => r.CreateRetryPolicy(It.IsAny<int>()))
            .Returns(retryPolicy);

         providerMock.Setup(p => p.CreateAsync(
            It.IsAny<Entity>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(Guid.NewGuid());

         providerMock.Setup(p => p.UpdateAsync(
            It.IsAny<Entity>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).Returns(Task.CompletedTask);

         // Act
         await _seedingService.SeedSampleDataAsync(providerMock.Object, 1);

         // Assert
         providerMock.Verify(p => p.CreateAsync(
            It.Is<Entity>(e => e.LogicalName == SystemConstants.DataverseEntities.Account),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         ), Times.Once);
      }
   }
}
