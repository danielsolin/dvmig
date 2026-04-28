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
      public async Task IsEnvironmentReadyAsync_ReturnsFalse_WhenSchemaNotFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             It.IsAny<string>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null);

         var result = await _service.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task IsEnvironmentReadyAsync_ReturnsFalse_WhenPluginNotFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         var emptyCollection = new EntityCollection();
         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q => q.EntityName == "pluginassembly"),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(emptyCollection);

         var result = await _service.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task IsEnvironmentReadyAsync_ReturnsFalse_WhenPluginTypeNotFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.MigrationFailure.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         var assemblyId = Guid.NewGuid();
         var assemblyEntity = new Entity("pluginassembly", assemblyId);
         var assemblyCollection = new EntityCollection(
             new[] { assemblyEntity }
         );
         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q => q.EntityName == "pluginassembly"),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(assemblyCollection);

         var emptyCollection = new EntityCollection();
         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q => q.EntityName == "plugintype"),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(emptyCollection);

         var result = await _service.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task IsEnvironmentReadyAsync_ReturnsTrue_WhenSchemaAndPluginAndStepsFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.MigrationFailure.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         var assemblyId = Guid.NewGuid();
         var assemblyCollection = new EntityCollection(
             new[] { new Entity("pluginassembly", assemblyId) }
         );
         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q => q.EntityName == "pluginassembly"),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(assemblyCollection);

         var pluginTypeId = Guid.NewGuid();
         var pluginTypeCollection = new EntityCollection(
             new[] { new Entity("plugintype", pluginTypeId) }
         );
         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q => q.EntityName == "plugintype"),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(pluginTypeCollection);

         var stepCollection = new EntityCollection(new[]
         {
                new Entity("sdkmessageprocessingstep", Guid.NewGuid()),
                new Entity("sdkmessageprocessingstep", Guid.NewGuid())
            });
         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q =>
                 q.EntityName == "sdkmessageprocessingstep"),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(stepCollection);

         var result = await _service.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.True(result);
      }

      [Fact]
      public async Task CreateSchemaAsync_CreatesEntity_WhenNotExists()
      {
         var entityMetadata = new EntityMetadata();
         typeof(EntityMetadata).GetProperty("Attributes")?.SetValue(
             entityMetadata,
             new AttributeMetadata[0]
         );

         // Mock dm_sourcedate
         _targetMock.SetupSequence(t => t.GetEntityMetadataAsync(
             SystemConstants.SourceDate.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null)
          .ReturnsAsync(entityMetadata);

         // Mock dm_migrationfailure
         _targetMock.SetupSequence(t => t.GetEntityMetadataAsync(
             SystemConstants.MigrationFailure.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null)
          .ReturnsAsync(entityMetadata);

         await _service.CreateSchemaAsync(_targetMock.Object, null);

         _targetMock.Verify(t => t.ExecuteAsync(
             It.Is<OrganizationRequest>(r => r.RequestName == "CreateEntity"),
             It.IsAny<CancellationToken>()), Times.Exactly(2)
         );
      }

      [Fact]
      public async Task CreateSchemaAsync_DoesNotCreateEntity_WhenExists()
      {
         var entityMetadata = new EntityMetadata();
         typeof(EntityMetadata).GetProperty("Attributes")?.SetValue(
             entityMetadata,
             new AttributeMetadata[0]
         );

         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             It.IsAny<string>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(entityMetadata);

         await _service.CreateSchemaAsync(_targetMock.Object, null);

         _targetMock.Verify(t => t.ExecuteAsync(
             It.Is<OrganizationRequest>(r => r.RequestName == "CreateEntity"),
             It.IsAny<CancellationToken>()), Times.Never
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
