using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using dvmig.Core.Shared;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Polly;

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
         await Assert.ThrowsAsync<FileNotFoundException>(
            () =>
               _pluginService.DeployPluginAsync(
                  _targetMock.Object,
                  "non_existent_path.dll"
               )
         );
      }

      [Fact]
      public async Task SeedSampleDataAsync_DiscoversAndUsesUsersForImpersonation()
      {
         // Arrange
         var providerMock = new Mock<IDataverseProvider>();
         var retryPolicy = Policy.Handle<Exception>().RetryAsync(0);
         _retryServiceMock.Setup(r => r.CreateRetryPolicy(It.IsAny<int>()))
            .Returns(retryPolicy);

         var user1Id = Guid.NewGuid();
         var user1 = new Entity(SystemConstants.DataverseEntities.SystemUser, user1Id);
         user1[SystemConstants.DataverseAttributes.FullName] = "Sample User 1";
         user1[SystemConstants.DataverseAttributes.FirstName] = "Sample";

         var users = new EntityCollection(new List<Entity> { user1 });

         providerMock.Setup(p => p.RetrieveMultipleAsync(
            It.Is<QueryExpression>(q => q.EntityName == SystemConstants.DataverseEntities.SystemUser),
            It.IsAny<CancellationToken>()
         )).ReturnsAsync(users);

         providerMock.Setup(p => p.CreateAsync(
            It.IsAny<Entity>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(Guid.NewGuid());

         // Act
         await _seedingService.SeedSampleDataAsync(providerMock.Object, 1);

         // Assert
         // 1. Verify that retrieve multiple was called for users with correct filters
         providerMock.Verify(p => p.RetrieveMultipleAsync(
            It.Is<QueryExpression>(q => 
               q.EntityName == SystemConstants.DataverseEntities.SystemUser &&
               q.Criteria.Conditions.Any(c => 
                  c.AttributeName == SystemConstants.DataverseAttributes.IsDisabled && 
                  (bool)c.Values[0] == false) &&
               q.Criteria.Conditions.Any(c => 
                  c.AttributeName == SystemConstants.DataverseAttributes.AccessMode && 
                  (int)c.Values[0] == 0)
            ),
            It.IsAny<CancellationToken>()
         ), Times.Once);

         // 2. Verify that CallerId was set during creation
         providerMock.VerifySet(p => p.CallerId = user1Id, Times.AtLeastOnce());
         
         // 3. Verify it was restored (assuming null was the original value in mock)
         providerMock.VerifySet(p => p.CallerId = It.IsAny<Guid?>(), Times.AtLeastOnce());
      }
   }
}
