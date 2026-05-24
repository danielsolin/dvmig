using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;

namespace dvmig.Tests
{
   public class EntityServiceTests
   {
      [Fact]
      public async Task GetMigrationEntitiesAsync_CanReturnCuratedEntities()
      {
         var provider = CreateProvider(
            CreateMetadata("account", isImportable: false),
            CreateMetadata("opportunity", isValidForAdvancedFind: false),
            CreateMetadata("queue"),
            CreateMetadata("msdyn_project", isLogicalEntity: true),
            CreateMetadata(
               "contoso_managedproject",
               isCustomEntity: true,
               isManaged: true
            ),
            CreateMetadata("custom_project", isCustomEntity: true)
         );

         var service = new EntityService(Mock.Of<ILogger>());

         var entities = await service.GetMigrationEntitiesAsync(
            provider.Object,
            includeHidden: false
         );

         Assert.Equal(
            ["account", "opportunity"],
            entities.Select(e => e.LogicalName)
         );
      }

      [Fact]
      public async Task GetMigrationEntitiesAsync_CuratedEntitiesExcludeDvmigInternalEntities()
      {
         var provider = CreateProvider(
            CreateMetadata(
               SystemConstants.SourceData.EntityLogicalName,
               isCustomEntity: true
            ),
            CreateMetadata(
               SystemConstants.MigrationFailure.EntityLogicalName,
               isCustomEntity: true
            )
         );

         var service = new EntityService(Mock.Of<ILogger>());

         var entities = await service.GetMigrationEntitiesAsync(
            provider.Object,
            includeHidden: false
         );

         Assert.Empty(entities);
      }

      [Fact]
      public async Task GetMigrationEntitiesAsync_CanReturnAllEntities()
      {
         var provider = CreateProvider(
            CreateMetadata("account", isImportable: false),
            CreateMetadata("opportunity", isValidForAdvancedFind: false),
            CreateMetadata("msdyn_project", isLogicalEntity: true),
            CreateMetadata(
               SystemConstants.SourceData.EntityLogicalName,
               isCustomEntity: true
            )
         );

         var service = new EntityService(Mock.Of<ILogger>());

         var entities = await service.GetMigrationEntitiesAsync(
            provider.Object,
            includeHidden: true
         );

         Assert.Equal(
            [
               "account",
               "dm_sourcedata",
               "msdyn_project",
               "opportunity"
            ],
            entities.Select(e => e.LogicalName)
         );
      }

      private static Mock<IDataverseProvider> CreateProvider(
         params EntityMetadata[] metadata
      )
      {
         var response = new RetrieveAllEntitiesResponse();
         response.Results["EntityMetadata"] = metadata;

         var provider = new Mock<IDataverseProvider>();
         provider
            .Setup(p => p.ExecuteAsync(
               It.IsAny<OrganizationRequest>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            ))
            .ReturnsAsync(response);

         return provider;
      }

      private static EntityMetadata CreateMetadata(
         string logicalName,
         bool isCustomEntity = false,
         bool isManaged = false,
         bool isIntersect = false,
         bool isValidForAdvancedFind = true,
         bool isImportable = true,
         bool isLogicalEntity = false
      )
      {
         var metadata = new EntityMetadata
         {
            LogicalName = logicalName
         };

         SetMetadataProperty(
            metadata,
            nameof(EntityMetadata.IsCustomEntity),
            isCustomEntity
         );
         SetMetadataProperty(
            metadata,
            nameof(EntityMetadata.IsIntersect),
            isIntersect
         );
         SetMetadataProperty(
            metadata,
            nameof(EntityMetadata.IsValidForAdvancedFind),
            isValidForAdvancedFind
         );
         SetMetadataProperty(
            metadata,
            nameof(EntityMetadata.IsImportable),
            isImportable
         );
         SetMetadataProperty(
            metadata,
            nameof(EntityMetadata.IsLogicalEntity),
            isLogicalEntity
         );
         SetMetadataProperty(
            metadata,
            nameof(EntityMetadata.IsManaged),
            isManaged
         );

         return metadata;
      }

      private static void SetMetadataProperty(
         EntityMetadata metadata,
         string propertyName,
         bool value
      )
      {
         typeof(EntityMetadata).GetProperty(propertyName)?.SetValue(
            metadata,
            value
         );
      }
   }
}
