using dvmig.Core.Interfaces;
using dvmig.Core.Shared;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;

namespace dvmig.Tests
{
   public class UserResolverTests
   {
      private readonly Mock<IDataverseProvider> _sourceMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly UserResolver _resolver;

      public UserResolverTests()
      {
         _sourceMock = new Mock<IDataverseProvider>();
         _targetMock = new Mock<IDataverseProvider>();
         _loggerMock = new Mock<ILogger>();

         _resolver = new UserResolver(
            _sourceMock.Object,
            _targetMock.Object,
            _loggerMock.Object
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
            SystemConstants.DataverseEntities.SystemUser,
            sourceId
         );

         var result = await _resolver.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);

         Assert.Equal(
            SystemConstants.DataverseEntities.SystemUser,
            result.LogicalName
         );
      }

      [Fact]
      public async Task MapUserAsync_ReturnsNull_WhenSourceUserNotFound()
      {
         var sourceId = Guid.NewGuid();

         var sourceRef = new EntityReference(
            SystemConstants.DataverseEntities.SystemUser,
            sourceId
         );

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               SystemConstants.DataverseEntities.SystemUser,
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
            SystemConstants.DataverseEntities.SystemUser,
            sourceId
         );

         var sourceEntity = new Entity(
            SystemConstants.DataverseEntities.SystemUser,
            sourceId
         );

         sourceEntity[
            SystemConstants.DataverseAttributes.InternalEmailAddress
         ] = "test@example.com";

         var targetEntity = new Entity(
            SystemConstants.DataverseEntities.SystemUser,
            targetId
         );

         var targetCollection = new EntityCollection(new[] { targetEntity });

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               SystemConstants.DataverseEntities.SystemUser,
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
                        SystemConstants.DataverseAttributes.InternalEmailAddress
                     ) &&
                     q.Values.Contains("test@example.com")
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
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
            SystemConstants.DataverseEntities.SystemUser,
            sourceId
         );

         var sourceEntity = new Entity(
            SystemConstants.DataverseEntities.SystemUser,
            sourceId
         );

         sourceEntity[SystemConstants.DataverseAttributes.DomainName] =
            "domain\\user";

         var targetCollection = new EntityCollection(
            new[]
            {
               new Entity(SystemConstants.DataverseEntities.SystemUser, targetId)
            }
         );

         var emptyCollection = new EntityCollection();

         _sourceMock.Setup(
            s => s.RetrieveAsync(
               SystemConstants.DataverseEntities.SystemUser,
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
                        SystemConstants.DataverseAttributes.InternalEmailAddress
                     )
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(emptyCollection);

         _targetMock.Setup(
            t => t.RetrieveMultipleAsync(
               It.Is<QueryByAttribute>(
                  q =>
                     q.Attributes.Contains(
                        SystemConstants.DataverseAttributes.DomainName
                     ) &&
                     q.Values.Contains("domain\\user")
               ),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(targetCollection);

         var result = await _resolver.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);
      }

      [Fact]
      public async Task MapAllSourceUsersAsync_MapsActiveUsers()
      {
         // Arrange
         var sourceUserId = Guid.NewGuid();
         var targetUserId = Guid.NewGuid();

         var sourceUser = new Entity(SystemConstants.DataverseEntities.SystemUser, sourceUserId);
         sourceUser[SystemConstants.DataverseAttributes.FullName] = "Source User";
         sourceUser[SystemConstants.DataverseAttributes.InternalEmailAddress] = "test@example.com";
         sourceUser[SystemConstants.DataverseAttributes.AccessMode] = new OptionSetValue(0); // Read-Write (Human)

         var sourceCollection = new EntityCollection(new[] { sourceUser });

         _sourceMock.Setup(s => s.RetrieveMultipleAsync(
            It.IsAny<QueryExpression>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(sourceCollection);

         var targetUser = new Entity(SystemConstants.DataverseEntities.SystemUser, targetUserId);
         targetUser[SystemConstants.DataverseAttributes.FullName] = "Target User";

         var targetCollection = new EntityCollection(new[] { targetUser });

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
            It.IsAny<QueryByAttribute>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(targetCollection);

         // Act
         await _resolver.MapAllSourceUsersAsync();

         // Assert
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
         // Arrange
         var sourceUserId = Guid.NewGuid();
         var sourceUser = new Entity(SystemConstants.DataverseEntities.SystemUser, sourceUserId);
         sourceUser[SystemConstants.DataverseAttributes.FullName] = "# Agent 365";
         sourceUser[SystemConstants.DataverseAttributes.InternalEmailAddress] = "agent@example.com";
         sourceUser[SystemConstants.DataverseAttributes.AccessMode] = new OptionSetValue(3); // Non-interactive

         var sourceCollection = new EntityCollection(new[] { sourceUser });

         _sourceMock.Setup(s => s.RetrieveMultipleAsync(
            It.IsAny<QueryExpression>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(sourceCollection);

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
            It.IsAny<QueryByAttribute>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(new EntityCollection());

         // Act
         await _resolver.MapAllSourceUsersAsync();

         // Assert
         var summaries = await _resolver.GetMappingSummaryAsync();
         Assert.Single(summaries);
         Assert.False(summaries[0].IsHuman);
      }

      [Fact]
      public async Task GetMappingSummaryAsync_ReturnsUnmapped_WhenResolutionFails()
      {
         // Arrange
         var sourceUserId = Guid.NewGuid();
         var sourceUser = new Entity(SystemConstants.DataverseEntities.SystemUser, sourceUserId);
         sourceUser[SystemConstants.DataverseAttributes.FullName] = "Lonely User";

         var sourceCollection = new EntityCollection(new[] { sourceUser });

         _sourceMock.Setup(s => s.RetrieveMultipleAsync(
            It.IsAny<QueryExpression>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(sourceCollection);

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
            It.IsAny<QueryByAttribute>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Guid?>()
         )).ReturnsAsync(new EntityCollection());

         // Act
         await _resolver.MapAllSourceUsersAsync();

         // Assert
         var summaries = await _resolver.GetMappingSummaryAsync();
         Assert.Single(summaries);
         Assert.Equal("Lonely User", summaries[0].SourceName);
         Assert.Equal("Unmapped", summaries[0].Status);
         Assert.Equal(Guid.Empty, summaries[0].TargetId);
      }
   }
}
