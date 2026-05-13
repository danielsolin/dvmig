using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

using Moq;

using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;
using static dvmig.Core.Shared.SystemConstants;


namespace dvmig.Tests
{
   public class ProvisioningTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IUserService> _userServiceMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly SeedingService _seedingService;

      public ProvisioningTests()
      {
         _loggerMock = new Mock<ILogger>();
         _userServiceMock = new Mock<IUserService>();
         _targetMock = new Mock<IDataverseProvider>();

         _userServiceMock.Setup(
            u => u.GetRealActiveUsersAsync(
               It.IsAny<IDataverseProvider>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new List<Guid>());

         _seedingService = new SeedingService(
            _loggerMock.Object,
            _userServiceMock.Object
         );
      }

      [Fact]
      public async Task InstallComponentsAsync_OrchestratesSetup()
      {
         var service = new EnvironmentService(_loggerMock.Object);
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
            await service.InstallComponentsAsync(_targetMock.Object);
         }
         catch (FileNotFoundException)
         {
         }
      }

      [Fact]
      public async Task SeedSampleDataAsync_CreatesRecords()
      {
         var providerMock = new Mock<IDataverseProvider>();

         providerMock.Setup(
            p => p.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(Guid.NewGuid());

         providerMock.Setup(
            p => p.UpdateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).Returns(Task.CompletedTask);

         await _seedingService.SeedSampleDataAsync(providerMock.Object, 1);

         providerMock.Verify(
            p => p.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        DataverseEntities.Account.Name
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.Once
         );
      }
   }
}
