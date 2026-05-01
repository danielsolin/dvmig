using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using Moq;
using Serilog;

namespace dvmig.Tests
{
   public class ProvisioningTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly PluginService _pluginService;

      public ProvisioningTests()
      {
         _loggerMock = new Mock<ILogger>();
         _targetMock = new Mock<IDataverseProvider>();
         _pluginService = new PluginService(_loggerMock.Object);
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
