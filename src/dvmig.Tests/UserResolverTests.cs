using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

using Moq;

using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Tests
{
   public class UserResolverTests
   {
      private readonly Mock<IDataverseProvider> _sourceMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly UserService _resolver;

      public UserResolverTests()
      {
         _sourceMock = new Mock<IDataverseProvider>();
         _targetMock = new Mock<IDataverseProvider>();
         _loggerMock = new Mock<ILogger>();

         _resolver = new UserService(
            _loggerMock.Object,
            _sourceMock.Object,
            _targetMock.Object
         );
      }

      [Fact]
      public async Task MapUserAsync_ReturnsNull_WhenSourceUserIsNull()
      {
         var result = await _resolver.MapUserAsync(null);

         Assert.Null(result);
      }

      [Fact]
      public async Task MapUserAsync_ReturnsCachedMapping_WhenPreviouslyMapped()
      {
         var sourceId = Guid.NewGuid();
         var targetId = Guid.NewGuid();

         _resolver.AddManualMapping(sourceId, targetId);

         var sourceRef = new EntityReference(
            DataverseEntities.SystemUser.Name,
            sourceId
         );

         var result = await _resolver.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);

         Assert.Equal(
            DataverseEntities.SystemUser.Name,
            result.LogicalName
         );
      }

      [Fact]
      public async Task MapUserAsync_ReturnsNull_WhenSourceUserNotFound()
      {
         var sourceId = Guid.NewGuid();

         var sourceRef = new EntityReference(
            DataverseEntities.SystemUser.Name,
            sourceId
         );

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               DataverseEntities.SystemUser.Name,
               sourceId,
               It.IsAny<string[]>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync((Entity?)null);

         var result = await _resolver.MapUserAsync(sourceRef);

         Assert.Null(result);
      }

      [Fact]
      public async Task MapUserAsync_MapsByInternalEmailAddress()
      {
         var sourceId = Guid.NewGuid();
         var targetId = Guid.NewGuid();

         var sourceRef = new EntityReference(
            DataverseEntities.SystemUser.Name,
            sourceId
         );

         var sourceEntity = new Entity(
            DataverseEntities.SystemUser.Name,
            sourceId
         );

         sourceEntity[
            DataverseAttributes.InternalEmailAddress
         ] = "test@example.com";

         var targetEntity = new Entity(
            DataverseEntities.SystemUser.Name,
            targetId
         );

         var targetCollection = new EntityCollection(new[] { targetEntity });

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               DataverseEntities.SystemUser.Name,
               sourceId,
               It.IsAny<string[]>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(sourceEntity);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q =>
                     q.Attributes.Contains(
                        DataverseAttributes.InternalEmailAddress
                     ) &&
                     q.Values.Contains("test@example.com")
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(targetCollection);

         var result = await _resolver.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);
      }

      [Fact]
      public async Task MapUserAsync_MapsByDomainName_WhenEmailNotFound()
      {
         var sourceId = Guid.NewGuid();
         var targetId = Guid.NewGuid();

         var sourceRef = new EntityReference(
            DataverseEntities.SystemUser.Name,
            sourceId
         );

         var sourceEntity = new Entity(
            DataverseEntities.SystemUser.Name,
            sourceId
         );

         sourceEntity[DataverseAttributes.DomainName] =
            "domain\\user";

         var targetCollection = new EntityCollection(
            new[]
            {
               new Entity(
                  DataverseEntities.SystemUser.Name,
                  targetId
               )
            }
         );

         var emptyCollection = new EntityCollection();

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               DataverseEntities.SystemUser.Name,
               sourceId,
               It.IsAny<string[]>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(sourceEntity);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q =>
                     q.Attributes.Contains(
                        DataverseAttributes.InternalEmailAddress
                     )
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(emptyCollection);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q =>
                     q.Attributes.Contains(
                        DataverseAttributes.DomainName
                     ) &&
                     q.Values.Contains("domain\\user")
               ),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(targetCollection);

         var result = await _resolver.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);
      }

      [Fact]
      public async Task MapAllSourceUsersAsync_MapsActiveUsers()
      {
         var sourceUserId = Guid.NewGuid();
         var targetUserId = Guid.NewGuid();

         var sourceUser = new Entity(
            DataverseEntities.SystemUser.Name,
            sourceUserId
         );

         sourceUser[DataverseAttributes.FullName] =
            "Source User";

         sourceUser[DataverseAttributes.InternalEmailAddress] =
            "test@example.com";

         sourceUser[DataverseAttributes.AccessMode] =
            new OptionSetValue(0);

         var sourceCollection = new EntityCollection(new[] { sourceUser });

         _sourceMock.Setup(
            s => s.RetrieveMultipleAsync(
               It.IsAny<QueryExpression>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(sourceCollection);

         var targetUser = new Entity(
            DataverseEntities.SystemUser.Name,
            targetUserId
         );

         targetUser[DataverseAttributes.FullName] =
            "Target User";

         var targetCollection = new EntityCollection(new[] { targetUser });

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryByAttribute>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(targetCollection);

         await _resolver.MapAllSourceUsersAsync();

         var summaries = await _resolver.GetMappingSummaryAsync();
         Assert.Single(summaries);
         Assert.Equal("Source User", summaries[0].SourceName);
         Assert.Equal("Target User", summaries[0].TargetName);
         Assert.Equal("Mapped", summaries[0].Status);
         Assert.True(summaries[0].IsHuman);
      }

      [Fact]
      public async Task MapAllSourceUsersAsync_IdentifiesSystemUsers()
      {
         var sourceUserId = Guid.NewGuid();

         var sourceUser = new Entity(
            DataverseEntities.SystemUser.Name,
            sourceUserId
         );

         sourceUser[DataverseAttributes.FullName] =
            "# Agent 365";

         sourceUser[DataverseAttributes.InternalEmailAddress] =
            "agent@example.com";

         sourceUser[DataverseAttributes.AccessMode] =
            new OptionSetValue(3);

         var sourceCollection = new EntityCollection(new[] { sourceUser });

         _sourceMock.Setup(
            s => s.RetrieveMultipleAsync(
               It.IsAny<QueryExpression>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(sourceCollection);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryByAttribute>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         await _resolver.MapAllSourceUsersAsync();

         var summaries = await _resolver.GetMappingSummaryAsync();
         Assert.Single(summaries);
         Assert.False(summaries[0].IsHuman);
      }

      [Fact]
      public async Task GetMappingSummary_ReturnsUnmapped_WhenResolutionFails()
      {
         var sourceUserId = Guid.NewGuid();

         var sourceUser = new Entity(
            DataverseEntities.SystemUser.Name,
            sourceUserId
         );

         sourceUser[DataverseAttributes.FullName] =
            "Lonely User";

         var sourceCollection = new EntityCollection(new[] { sourceUser });

         _sourceMock.Setup(
            s => s.RetrieveMultipleAsync(
               It.IsAny<QueryExpression>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(sourceCollection);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.IsAny<QueryByAttribute>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         await _resolver.MapAllSourceUsersAsync();

         var summaries = await _resolver.GetMappingSummaryAsync();
         Assert.Single(summaries);
         Assert.Equal("Lonely User", summaries[0].SourceName);
         Assert.Equal("Unmapped", summaries[0].Status);
         Assert.Equal(Guid.Empty, summaries[0].TargetId);
      }
   }
}
