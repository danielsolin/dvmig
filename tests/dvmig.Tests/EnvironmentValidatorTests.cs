using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;

namespace dvmig.Tests
{
   public class EnvironmentValidatorTests
   {
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly EnvironmentValidator _validator;

      public EnvironmentValidatorTests()
      {
         _targetMock = new Mock<IDataverseProvider>();
         _validator = new EnvironmentValidator();
      }

      [Fact]
      public async Task
          IsEnvironmentReadyAsync_ReturnsFalse_WhenSchemaNotFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             It.IsAny<string>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null);

         var result = await _validator.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
          IsEnvironmentReadyAsync_ReturnsFalse_WhenPluginNotFound()
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

         var result = await _validator.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
          IsEnvironmentReadyAsync_ReturnsFalse_WhenPluginTypeNotFound()
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

         var result = await _validator.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
          IsEnvironmentReadyAsync_ReturnsTrue_WhenSchemaAndPluginAndStepsFound()
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

         var result = await _validator.IsEnvironmentReadyAsync(
             _targetMock.Object
         );

         Assert.True(result);
      }
   }
}