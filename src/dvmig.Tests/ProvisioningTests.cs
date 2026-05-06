using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Moq;

namespace dvmig.Tests
{
   public class ProvisioningTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly PluginService _pluginService;
      private readonly SeedingService _seedingService;

      public ProvisioningTests()
      {
         _loggerMock = new Mock<ILogger>();
         _targetMock = new Mock<IDataverseProvider>();
         _pluginService = new PluginService(_loggerMock.Object);

         _seedingService = new SeedingService(
            _loggerMock.Object
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

         providerMock.Setup(
            p => p.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(Guid.NewGuid());

         providerMock.Setup(
            p => p.UpdateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).Returns(Task.CompletedTask);

         // Act
         await _seedingService.SeedSampleDataAsync(providerMock.Object, 1);

         // Assert
         providerMock.Verify(
            p => p.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        SystemConstants.DataverseEntities.Account.Name
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.Once
         );
      }
   }
}
