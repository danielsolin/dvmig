using dvmig.Core.Settings;
using dvmig.Core.Shared;

namespace dvmig.Tests
{
   public class StringMaskerTests
   {
      [Theory]
      [InlineData(
         "AuthType=OAuth;Url=https://contoso.crm.dynamics.com;AppId=123", 
         "contoso.crm.dynamics.com"
      )]
      [InlineData(
         "ServiceUri=https://test.crm4.dynamics.com/;Token=abc", 
         "test.crm4.dynamics.com"
      )]
      [InlineData(
         "Server=http://myserver/org;User=admin", 
         "myserver/org"
      )]
      public void MaskConnectionString_ReturnsOnlyUrlWithoutProtocol(
         string input, 
         string expected
      )
      {
         // Act
         var result = StringMasker.MaskConnectionString(input);

         // Assert
         Assert.Equal(expected, result);
      }

      [Fact]
      public void MaskConnectionString_ReturnsUnknown_WhenNoUrlFound()
      {
         // Arrange
         var input = "NoUrlHere=True";
         var expected = SystemConstants.Connection.UnknownEnvironment;

         // Act
         var result = StringMasker.MaskConnectionString(input);

         // Assert
         Assert.Equal(expected, result);
      }
   }
}
