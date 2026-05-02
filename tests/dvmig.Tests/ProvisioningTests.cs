using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using Moq;

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
         await Assert.ThrowsAsync<FileNotFoundException>(() =>
             _pluginService.DeployPluginAsync(
                 _targetMock.Object,
                 "non_existent_path.dll"
             )
         );
      }
   }
}
