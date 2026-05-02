using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;

namespace dvmig.Tests
{
   public class ValidationServiceTests
   {
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly ValidationService _service;

      public ValidationServiceTests()
      {
         _targetMock = new Mock<IDataverseProvider>();
         _service = new ValidationService();
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsFalse_WhenFailureSchemaMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.MigrationFailure.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((EntityMetadata?)null);

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsFalse_WhenSourceDateSchemaMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.MigrationFailure.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SystemConstants.SourceDate.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((EntityMetadata?)null);

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsFalse_WhenAssemblyMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.AssemblyEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsFalse_WhenPluginTypeMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.AssemblyEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { new Entity("pluginassembly") }
         });

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.TypeEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsFalse_WhenStepsMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.AssemblyEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { new Entity("pluginassembly") }
         });

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.TypeEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { new Entity("plugintype") { Id = Guid.NewGuid() } }
         });

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.StepEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsTrue_WhenAllComponentsPresent()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.AssemblyEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { new Entity("pluginassembly") }
         });

         var typeId = Guid.NewGuid();
         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.TypeEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { new Entity("plugintype") { Id = typeId } }
         });

         var step1 = new Entity(SystemConstants.PluginRegistration.StepEntity);
         step1[SystemConstants.PluginRegistration.MessageName] = "Create";
         var step2 = new Entity(SystemConstants.PluginRegistration.StepEntity);
         step2[SystemConstants.PluginRegistration.MessageName] = "Update";

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName == 
                     SystemConstants.PluginRegistration.StepEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { step1, step2 }
         });

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.True(result);
      }
   }
}
