using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

using Moq;

using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Tests
{
   public class SyncEngineTests
   {
      private readonly Mock<IDataverseProvider> _sourceMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<IUserService> _userResolverMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly SyncEngine _engine;

      public SyncEngineTests()
      {
         _sourceMock = new Mock<IDataverseProvider>();
         _targetMock = new Mock<IDataverseProvider>();
         _userResolverMock = new Mock<IUserService>();
         _loggerMock = new Mock<ILogger>();

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryExpression>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         var defaultMetadata = new EntityMetadata();

         typeof(EntityMetadata).GetProperty("Attributes")?.SetValue(
            defaultMetadata,
            new AttributeMetadata[0]
         );

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(defaultMetadata);

         var entityService = new EntityService(
            _loggerMock.Object,
            _targetMock.Object
         );
         var syncStateService = new SyncStateService();

         _engine = new SyncEngine(
            _sourceMock.Object,
            _targetMock.Object,
            _userResolverMock.Object,
            _loggerMock.Object,
            entityService,
            syncStateService
         );

         _userResolverMock.Setup(
            m => m.MapUserAsync(
               It.IsAny<EntityReference>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((EntityReference r, CancellationToken ct) => r);
      }

      [Fact]
      public async Task SyncRecordAsync_StripReadOnly_OnForbidden()
      {
         var accountId = Guid.NewGuid();

         var account = new Entity(
            DataverseEntities.Account.Name,
            accountId
         );

         account[DataverseAttributes.Name] = "Test Account";
         account["readonlyfield"] = "Value";

         int callCount = 0;

         _targetMock.Setup(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        DataverseEntities.Account.Name
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).Returns<Entity, CancellationToken, Guid?>(
            (e, ct, callerId) =>
            {
               callCount++;

               if (e.Attributes.Contains("readonlyfield"))
               {
                  throw new Exception(
                     "The property 'readonlyfield' cannot be modified."
                  );
               }

               return Task.FromResult(accountId);
            }
         );

         var options = new SyncOptions();

         var (result, _) = await _engine.SyncRecordAsync(
            account,
            options
         );

         Assert.True(result);
         Assert.Equal(2, callCount);
      }

      [Fact]
      public async Task SyncRecordAsync_SyncDependency_WhenMissing()
      {
         var accountId = Guid.NewGuid();
         var contactId = Guid.NewGuid();

         var contact = new Entity(
            DataverseEntities.Contact.Name,
            contactId
         );

         contact[DataverseAttributes.ParentCustomerId] =
            new EntityReference(
               DataverseEntities.Account.Name,
               accountId
            );

         var account = new Entity(
            DataverseEntities.Account.Name,
            accountId
         );

         account[DataverseAttributes.Name] = "Test Account";

         int contactCreateCalls = 0;

         _targetMock.Setup(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        DataverseEntities.Contact.Name
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).Returns<Entity, CancellationToken, Guid?>(
            (e, ct, callerId) =>
            {
               contactCreateCalls++;

               if (contactCreateCalls == 1)
               {
                  throw new Exception(
                     "account with Id=" + accountId + " does not exist"
                  );
               }

               return Task.FromResult(contactId);
            }
         );

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               DataverseEntities.Account.Name,
               accountId,
               It.IsAny<string[]>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(account);

         _targetMock.Setup(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        DataverseEntities.Account.Name
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(accountId);

         var options = new SyncOptions();

         var (result, _) = await _engine.SyncRecordAsync(
            contact,
            options
         );

         Assert.True(result);
         Assert.Equal(2, contactCreateCalls);

         _targetMock.Verify(
            t => t.CreateAsync(
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

      [Fact]
      public async Task SyncRecordAsync_CallAssociate_WhenEntityIsIntersect()
      {
         var relName = "new_account_contact";
         var accountId = Guid.NewGuid();
         var contactId = Guid.NewGuid();

         var intersectEntity = new Entity(relName, Guid.NewGuid());

         intersectEntity["accountid"] = new EntityReference(
            DataverseEntities.Account.Name,
            accountId
         );

         intersectEntity["contactid"] = new EntityReference(
            DataverseEntities.Contact.Name,
            contactId
         );

         var metadata = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata
         {
            LogicalName = relName
         };

         typeof(Microsoft.Xrm.Sdk.Metadata.EntityMetadata)
            .GetProperty(nameof(metadata.IsIntersect))
            ?.SetValue(metadata, true);

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               relName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(metadata);

         _targetMock.Setup(
            t => t.ExecuteAsync(
               It.IsAny<AssociateRequest>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(new AssociateResponse());

         var options = new SyncOptions();

         var (result, _) = await _engine.SyncRecordAsync(
            intersectEntity,
            options
         );

         Assert.True(result);

         _targetMock.Verify(
            t => t.ExecuteAsync(
               It.IsAny<AssociateRequest>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.Once
         );
      }

      [Fact]
      public async Task SyncRecordAsync_MapUser_WhenAttributeIsUserField()
      {
         var sourceUserId = Guid.NewGuid();
         var targetUserId = Guid.NewGuid();

         var sourceUserRef = new EntityReference(
            DataverseEntities.SystemUser.Name,
            sourceUserId
         );

         var targetUserRef = new EntityReference(
            DataverseEntities.SystemUser.Name,
            targetUserId
         );

         var account = new Entity(
            DataverseEntities.Account.Name,
            Guid.NewGuid()
         );

         account[DataverseAttributes.OwnerId] = sourceUserRef;

         _userResolverMock.Setup(
            m => m.MapUserAsync(
               sourceUserRef,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(targetUserRef);

         _targetMock.Setup(
            t => t.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(account.Id);

         var options = new SyncOptions();

         await _engine.SyncRecordAsync(account, options);

         _targetMock.Verify(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     ((EntityReference)
                        e[DataverseAttributes.OwnerId]).Id ==
                        targetUserId
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.Once
         );
      }

      [Fact]
      public async Task PreserveAuditData_WhenOptionIsEnabled()
      {
         var account = new Entity(
            DataverseEntities.Account.Name,
            Guid.NewGuid()
         );

         account[DataverseAttributes.Name] = "Audit Test";

         account[DataverseAttributes.CreatedOn] =
            DateTime.UtcNow;

         _targetMock.Setup(
            t => t.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(account.Id);

         var options = new SyncOptions
         {
            PreserveAuditData = true
         };

         await _engine.SyncRecordAsync(account, options);

         _targetMock.Verify(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        SourceData.EntityLogicalName
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.AtLeastOnce
         );
      }

      [Fact]
      public async Task SyncRecordAsync_UpdatesExisting_OnDuplicate()
      {
         var accountId = Guid.NewGuid();

         var account = new Entity(
            DataverseEntities.Account.Name,
            accountId
         )
         {
            [DataverseAttributes.Name] = "Existing Account",
            [DataverseAttributes.Telephone1] = "12345"
         };

         int createCalls = 0;

         _targetMock.Setup(
            t => t.CreateAsync(
               It.IsAny<Entity>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).Returns<Entity, CancellationToken, Guid?>(
            (e, ct, callerId) =>
            {
               createCalls++;

               throw new Exception(
                  $"A record with this ID " +
                  $"{ErrorKeywords.AlreadyExists}."
               );
            }
         );

         _targetMock.Setup(
            t => t.UpdateAsync(
               It.Is<Entity>(e => e.Id == accountId),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).Returns(Task.CompletedTask);

         var options = new SyncOptions();

         var (result, _) = await _engine.SyncRecordAsync(
            account,
            options
         );

         Assert.True(result);
         Assert.Equal(1, createCalls);

         _targetMock.Verify(
            t => t.UpdateAsync(
               It.Is<Entity>(
                  e =>
                     (string)
                        e[DataverseAttributes.Telephone1] ==
                           "12345"
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.Once
         );
      }

      [Fact]
      public async Task SyncAsync_RegistersFailureRecord_WhenCreateFails()
      {
         var accountId = Guid.NewGuid();

         var account = new Entity(
            DataverseEntities.Account.Name,
            accountId
         )
         {
            [DataverseAttributes.Name] = "Failure Account"
         };

         var metadata = new Microsoft.Xrm.Sdk.Metadata.EntityMetadata
         {
            LogicalName = DataverseEntities.Account.Name
         };

         _targetMock.Setup(
            t => t.GetEntityMetadataAsync(
               DataverseEntities.Account.Name,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(metadata);

         _targetMock.Setup(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        DataverseEntities.Account.Name
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ThrowsAsync(new Exception("Create failed"));

         _targetMock.Setup(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        MigrationFailure.EntityLogicalName
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(Guid.NewGuid());

         var options = new SyncOptions();

         await _engine.SyncRecordAndReportAsync(
            account,
            options,
            null,
            CancellationToken.None
         );

         _targetMock.Verify(
            t => t.CreateAsync(
               It.Is<Entity>(
                  e =>
                     e.LogicalName ==
                        MigrationFailure.EntityLogicalName
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ),
            Times.Once
         );
      }
   }
}
