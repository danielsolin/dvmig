using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using Moq;
using Serilog;

namespace dvmig.Tests
{
   public class SetupServiceTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly SetupService _service;

      public SetupServiceTests()
      {
         _loggerMock = new Mock<ILogger>();
         _targetMock = new Mock<IDataverseProvider>();

         var validator = new EnvironmentValidator();
         var schemaManager = new SchemaManager(_loggerMock.Object);
         var pluginDeployer = new PluginDeployer(_loggerMock.Object);

         _service = new SetupService(
             validator,
             schemaManager,
             pluginDeployer,
             _loggerMock.Object
         );
      }

      [Fact]
      public async Task DeployPluginAsync_ThrowsFileNotFound_WhenDllNotFound()
      {
         await Assert.ThrowsAsync<FileNotFoundException>(() =>
             _service.DeployPluginAsync(
                 _targetMock.Object
             )
         );
      }
   }
}