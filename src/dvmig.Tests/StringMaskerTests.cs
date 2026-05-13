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
         var result = StringMasker.MaskConnectionString(input);

         Assert.Equal(expected, result);
      }

      [Fact]
      public void MaskConnectionString_ReturnsOriginal_WhenNoSensitiveData()
      {
         var input = "Server=http://myserver/org;User=admin";

         var result = StringMasker.MaskConnectionString(input);

         Assert.Equal(input, result);
      }
   }
}
