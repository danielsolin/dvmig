using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

using Moq;

using dvmig.Core.Interfaces;
using dvmig.Core.Provisioning;

namespace dvmig.Tests
{
   public class WipeDataServiceTests
   {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<IDataverseProvider> _providerMock;
      private readonly WipeDataService _service;

      public WipeDataServiceTests()
      {
         _loggerMock = new Mock<ILogger>();
         _providerMock = new Mock<IDataverseProvider>();
         _service = new WipeDataService(_loggerMock.Object);
      }

      [Fact]
      public async Task WipeEntitiesAsync_PerformsTwoPasses()
      {
         var entityName = "account";
         var entities = new List<string> { entityName };

         var metadata = new EntityMetadata();

         _providerMock.Setup(
            p => p.GetEntityMetadataAsync(
               entityName,
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(metadata);

         _providerMock.Setup(
            p => p.RetrieveMultipleAsync(
               It.IsAny<QueryBase>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         _providerMock.Setup(
            p => p.ExecuteAsync(
               It.IsAny<RetrieveEntityRequest>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(new RetrieveEntityResponse
         {
            Results = { ["EntityMetadata"] = metadata }
         });

         await _service.WipeEntitiesAsync(_providerMock.Object, entities);

         _loggerMock.Verify(
            l => l.Information(It.Is<string>(s => s.Contains("Pass 1/2"))),
            Times.Once
         );
         _loggerMock.Verify(
            l => l.Information(It.Is<string>(s => s.Contains("Pass 2/2"))),
            Times.Once
         );
      }

      [Fact]
      public async Task WipeEntitiesAsync_ReportsStatus()
      {
         var entityName = "account";
         var entities = new List<string> { entityName };
         var statusMock = new Mock<IProgress<string>>();
         var metadata = new EntityMetadata();

         _providerMock.Setup(
            p => p.ExecuteAsync(
               It.IsAny<RetrieveEntityRequest>(),
               It.IsAny<CancellationToken>(),
               It.IsAny<Guid?>()
            )
         ).ReturnsAsync(new RetrieveEntityResponse
         {
            Results = { ["EntityMetadata"] = metadata }
         });

         _providerMock.Setup(
            p => p.RetrieveMultipleAsync(
               It.IsAny<QueryBase>(),
               It.IsAny<CancellationToken>()
            )
         ).ReturnsAsync(new EntityCollection());

         await _service.WipeEntitiesAsync(
            _providerMock.Object,
            entities,
            status: statusMock.Object
         );

         statusMock.Verify(
            s => s.Report(It.Is<string>(st => st.Contains("Disassociating"))),
            Times.AtLeastOnce
         );
         statusMock.Verify(
            s => s.Report(It.Is<string>(st => st.Contains("Cleaning"))),
            Times.AtLeastOnce
         );
      }
   }
}
