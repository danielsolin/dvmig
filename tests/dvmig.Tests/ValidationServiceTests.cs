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
          ValidateTargetEnvironmentAsync_ReturnsFalse_WhenSchemaNotFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             It.IsAny<string>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((EntityMetadata?)null);

         var result = await _service.ValidateTargetEnvironmentAsync(
             _targetMock.Object
         );

         Assert.False(result);
      }

      [Fact]
      public async Task
          ValidateTargetEnvironmentAsync_ReturnsTrue_WhenSchemaFound()
      {
         _targetMock.Setup(t => t.GetEntityMetadataAsync(
             SystemConstants.MigrationFailure.EntityLogicalName,
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(new EntityMetadata());

         var result = await _service.ValidateTargetEnvironmentAsync(
             _targetMock.Object
         );

         Assert.True(result);
      }
   }
}
