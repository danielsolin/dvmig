using System.Collections.Concurrent;
using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;

namespace dvmig.Tests
{
   public class CircularDependencyTests
   {
      private readonly Mock<IDataverseProvider> _sourceMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<IUserService> _userResolverMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly SyncEngine _engine;
      private readonly SyncStateService _syncStateService;

      public CircularDependencyTests()
      {
         _sourceMock = new Mock<IDataverseProvider>();
         _targetMock = new Mock<IDataverseProvider>();
         _userResolverMock = new Mock<IUserService>();
         _loggerMock = new Mock<ILogger>();

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((string logicalName, CancellationToken ct) => 
            new EntityMetadata { LogicalName = logicalName });

         var entityService = new EntityService(
            _loggerMock.Object,
            _targetMock.Object
         );
         _syncStateService = new SyncStateService();

         _engine = new SyncEngine(
            _sourceMock.Object,
            _targetMock.Object,
            _userResolverMock.Object,
            _loggerMock.Object,
            entityService,
            _syncStateService
         );

         _userResolverMock.Setup(
            m => m.MapUserAsync(
               It.IsAny<EntityReference>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((EntityReference r, CancellationToken ct) => r);
      }

      [Fact]
      public async Task SyncRecordAsync_ShouldHandleCircularDependency_AndPopulateAllFields()
      {
         // Arrange
         var accountId = Guid.NewGuid();
         var contactId = Guid.NewGuid();

         var account = new Entity("account", accountId);
         account["name"] = "Account A";
         account["primarycontactid"] = new EntityReference("contact", contactId);

         var contact = new Entity("contact", contactId);
         contact["lastname"] = "Contact C";
         contact["parentcustomerid"] = new EntityReference("account", accountId);

         _sourceMock.Setup(s => s.RetrieveAsync("contact", contactId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contact);
         _sourceMock.Setup(s => s.RetrieveAsync("account", accountId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

         bool accountCreated = false;
         bool contactCreated = false;
         bool accountUpdatedWithContact = false;

         _targetMock.Setup(t => t.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Returns<Entity, CancellationToken, Guid?>((e, ct, callerId) =>
            {
               if (e.LogicalName == "account")
               {
                  if (e.Contains("primarycontactid") && !contactCreated)
                  {
                     throw new Exception($"Entity contact With Id = {contactId} {SystemConstants.ErrorKeywords.DoesNotExist}");
                  }
                  accountCreated = true;
                  return Task.FromResult(accountId);
               }
               if (e.LogicalName == "contact")
               {
                  if (e.Contains("parentcustomerid") && !accountCreated)
                  {
                     throw new Exception($"Entity account With Id = {accountId} {SystemConstants.ErrorKeywords.DoesNotExist}");
                  }
                  contactCreated = true;
                  return Task.FromResult(contactId);
               }
               return Task.FromResult(Guid.NewGuid());
            });

         _targetMock.Setup(t => t.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Returns<Entity, CancellationToken, Guid?>((e, ct, callerId) =>
            {
               if (e.LogicalName == "account" && e.Contains("primarycontactid"))
               {
                  accountUpdatedWithContact = true;
               }
               return Task.CompletedTask;
            });

         var options = new SyncOptions { StripMissingDependencies = true };

         // Act
         await _engine.SyncRecordAsync(account, options);

         // Assert
         Assert.True(accountCreated, "Account should be created");
         Assert.True(contactCreated, "Contact should be created");
         Assert.True(accountUpdatedWithContact, "Account should be updated with Primary Contact");
      }
   }
}
