using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Serilog;

namespace dvmig.Tests
{
   public class UserMapperTests
   {
      private readonly Mock<IDataverseProvider> _sourceMock;
      private readonly Mock<IDataverseProvider> _targetMock;
      private readonly Mock<ILogger> _loggerMock;
      private readonly UserMapper _mapper;

      public UserMapperTests()
      {
         _sourceMock = new Mock<IDataverseProvider>();
         _targetMock = new Mock<IDataverseProvider>();
         _loggerMock = new Mock<ILogger>();
         _mapper = new UserMapper(
             _sourceMock.Object,
             _targetMock.Object,
             _loggerMock.Object
         );
      }

      [Fact]
      public async Task MapUserAsync_ReturnsNull_WhenSourceUserIsNull()
      {
         var result = await _mapper.MapUserAsync(null);
         Assert.Null(result);
      }

      [Fact]
      public async Task MapUserAsync_ReturnsCachedMapping_WhenPreviouslyMapped()
      {
         var sourceId = Guid.NewGuid();
         var targetId = Guid.NewGuid();

         _mapper.AddManualMapping(sourceId, targetId);

         var sourceRef = new EntityReference("systemuser", sourceId);
         var result = await _mapper.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);
         Assert.Equal("systemuser", result.LogicalName);
      }

      [Fact]
      public async Task MapUserAsync_ReturnsNull_WhenSourceUserNotFound()
      {
         var sourceId = Guid.NewGuid();
         var sourceRef = new EntityReference("systemuser", sourceId);

         _sourceMock.Setup(s => s.RetrieveAsync(
             "systemuser",
             sourceId,
             It.IsAny<string[]>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync((Entity?)null);

         var result = await _mapper.MapUserAsync(sourceRef);

         Assert.Null(result);
      }

      [Fact]
      public async Task MapUserAsync_MapsByInternalEmailAddress()
      {
         var sourceId = Guid.NewGuid();
         var targetId = Guid.NewGuid();
         var sourceRef = new EntityReference("systemuser", sourceId);

         var sourceEntity = new Entity("systemuser", sourceId);
         sourceEntity["internalemailaddress"] = "test@example.com";

         var targetEntity = new Entity("systemuser", targetId);
         var targetCollection = new EntityCollection(new[] { targetEntity });

         _sourceMock.Setup(s => s.RetrieveAsync(
             "systemuser",
             sourceId,
             It.IsAny<string[]>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(sourceEntity);

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q =>
                 q.Attributes.Contains("internalemailaddress") &&
                 q.Values.Contains("test@example.com")),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(targetCollection);

         var result = await _mapper.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);
      }

      [Fact]
      public async Task MapUserAsync_MapsByDomainName_WhenEmailNotFound()
      {
         var sourceId = Guid.NewGuid();
         var targetId = Guid.NewGuid();
         var sourceRef = new EntityReference("systemuser", sourceId);

         var sourceEntity = new Entity("systemuser", sourceId);
         sourceEntity["domainname"] = "domain\\user";

         var targetCollection = new EntityCollection(
             new[] { new Entity("systemuser", targetId) }
         );
         var emptyCollection = new EntityCollection();

         _sourceMock.Setup(s => s.RetrieveAsync(
             "systemuser",
             sourceId,
             It.IsAny<string[]>(),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(sourceEntity);

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q =>
                 q.Attributes.Contains("internalemailaddress")),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(emptyCollection);

         _targetMock.Setup(t => t.RetrieveMultipleAsync(
             It.Is<QueryByAttribute>(q =>
                 q.Attributes.Contains("domainname") &&
                 q.Values.Contains("domain\\user")),
             It.IsAny<CancellationToken>())
         ).ReturnsAsync(targetCollection);

         var result = await _mapper.MapUserAsync(sourceRef);

         Assert.NotNull(result);
         Assert.Equal(targetId, result.Id);
      }
   }
}
