using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;
using Serilog;

namespace dvmig.Tests
{
   public class SchemaServiceTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly SchemaService _schemaService;

      public SchemaServiceTests()
      {
         _loggerMock = new Mock<ILogger>();
         _targetMock = new Mock<IDataverseProvider>();
         _schemaService = new SchemaService(_loggerMock.Object);
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

         await _schemaService.CreateSchemaAsync(_targetMock.Object, null);

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

         await _schemaService.CreateSchemaAsync(_targetMock.Object, null);

         _targetMock.Verify(t => t.ExecuteAsync(
             It.Is<OrganizationRequest>(r => r.RequestName == "CreateEntity"),
             It.IsAny<CancellationToken>()), Times.Never
         );
      }
   }
}
