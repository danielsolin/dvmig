using dvmig.Core.Synchronization;

namespace dvmig.Tests
{
   public class SyncTimerTests
   {
      [Theory]
      [InlineData(20.53, "20.53s")]
      [InlineData(65, "1m 5s")]
      [InlineData(3665, "1h 1m 5s")]
      public void Format_ReturnsReadableDuration(
         double totalSeconds,
         string expected
      )
      {
         var elapsed = TimeSpan.FromSeconds(totalSeconds);

         var formatted = SyncTimer.Format(elapsed);

         Assert.Equal(expected, formatted);
      }
   }
}
