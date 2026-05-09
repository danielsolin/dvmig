using dvmig.Core.Settings;

namespace dvmig.Tests
{
   public class StringMaskerTests
   {
      [Theory]
      [InlineData(
         "AuthType=OAuth;Url=https://contoso.crm.dynamics.com;AppId=123",
         "AuthType=OAuth;Url=https://contoso.crm.dynamics.com;AppId=123"
      )]
      [InlineData(
         "ServiceUri=https://test.crm4.dynamics.com/;Token=abc",
         "ServiceUri=https://test.crm4.dynamics.com/;Token=********"
      )]
      public void MaskConnectionString_ReturnsMaskedString(
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
      public void MaskConnectionString_ReturnsOriginal_WhenNoSensitiveData()
      {
         // Arrange
         var input = "Server=http://myserver/org;User=admin";

         // Act
         var result = StringMasker.MaskConnectionString(input);

         // Assert
         Assert.Equal(input, result);
      }
   }
}
