using System.Diagnostics;
using System.Globalization;

namespace dvmig.Core.Synchronization
{
   /// <summary>
   /// Shared sync runtime timer and duration formatter for CLI and XTB hosts.
   /// </summary>
   public sealed class SyncTimer
   {
      private readonly Stopwatch _stopwatch;

      private SyncTimer()
      {
         _stopwatch = Stopwatch.StartNew();
      }

      public TimeSpan Elapsed => _stopwatch.Elapsed;

      public static SyncTimer StartNew()
      {
         return new SyncTimer();
      }

      public TimeSpan Stop()
      {
         if (_stopwatch.IsRunning)
            _stopwatch.Stop();

         return _stopwatch.Elapsed;
      }

      public string StopAndFormat()
      {
         return Format(Stop());
      }

      public static string Format(TimeSpan elapsed)
      {
         if (elapsed.TotalMinutes < 1)
            return elapsed.TotalSeconds.ToString(
               "0.##",
               CultureInfo.InvariantCulture
            ) + "s";

         if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

         return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m " +
            $"{elapsed.Seconds}s";
      }
   }
}
