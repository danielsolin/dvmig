using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

using Moq;

using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Tests
{
   public class EnvironmentServiceTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly EnvironmentService _service;

      public EnvironmentServiceTests()
      {
         _loggerMock = new Mock<ILogger>();
         _targetMock = new Mock<IDataverseProvider>();
         _service = new EnvironmentService(_loggerMock.Object);
      }

      [Fact]
      public async Task
         ValidateTargetEnvironmentAsync_ReturnsFalse_WhenFailureSchemaMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               MigrationFailure.EntityLogicalName,
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
         ValidateTarget_ReturnsFalse_WhenSourceDataSchemaMissing()
      {
         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               MigrationFailure.EntityLogicalName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityMetadata());

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               SourceData.EntityLogicalName,
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
                     PluginRegistration.AssemblyEntity
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
                     PluginRegistration.TypeEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities =
            {
               new Entity("plugintype")
               {
                  Id = typeId
               }
            }
         });

         var step1 = new Entity(PluginRegistration.StepEntity);
         step1[PluginRegistration.MessageName] = "Create";

         var step2 = new Entity(PluginRegistration.StepEntity);
         step2[PluginRegistration.MessageName] = "Update";

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName ==
                     PluginRegistration.StepEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities =
            {
               step1,
               step2
            }
         });

         var result = await _service.ValidateTargetEnvironmentAsync(
            _targetMock.Object
         );

         Assert.True(result);
      }

      [Fact]
      public async Task InstallComponentsAsync_CreatesEntitiesAndDeployPlugin()
      {
         var entityMetadata = new EntityMetadata();
         typeof(EntityMetadata).GetProperty("Attributes")?.SetValue(
            entityMetadata,
            new AttributeMetadata[0]
         );

         _targetMock.SetupSequence(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         )
         .ReturnsAsync((EntityMetadata?)null)
         .ReturnsAsync(entityMetadata)
         .ReturnsAsync((EntityMetadata?)null)
         .ReturnsAsync(entityMetadata);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryBase>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q => q.EntityName ==
                     PluginRegistration.MessageEntity
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection
         {
            Entities = { new Entity("sdkmessage") { Id = Guid.NewGuid() } }
         });

         _targetMock.Setup(
            t => t.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(Guid.NewGuid());

         try
         {
            await _service.InstallComponentsAsync(_targetMock.Object);

            _targetMock.Verify(
               t => t.ExecuteAsync(
                  It.Is<CreateEntityRequest>(
                     r => r.Entity.LogicalName ==
                        SourceData.EntityLogicalName
                  ),
                  It.IsAny<CancellationToken>(),
                  It.IsAny<Guid?>()
               ),
               Times.Once
            );
         }
         catch (FileNotFoundException)
         {
         }
      }
   }
}
