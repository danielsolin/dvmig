using dvmig.Core.Shared;
using Spectre.Console;

namespace dvmig.Cli.Providers
{
   /// <summary>
   /// Provides a bridge between the core migration progress and the Spectre.Console UI,
   /// handling rate calculations and markup formatting.
   /// </summary>
   public class MigrationProgressProvider
   {
      private readonly ProgressTask _task;
      private readonly int _maxThreads;
      private readonly string _actionTitle;
      private readonly string _displayName;
      private readonly long _totalCount;
      private readonly object _progressLock = new object();
      private readonly System.Diagnostics.Stopwatch _sw;

      private int _processed;
      private int _failedCount;
      private DateTime _lastUpdate = DateTime.MinValue;

      /// <summary>
      /// Initializes a new instance of the <see cref="MigrationProgressProvider"/> class.
      /// </summary>
      public MigrationProgressProvider(
         ProgressTask task,
         int maxThreads,
         string actionTitle,
         string displayName,
         long totalCount,
         int initialProcessed = 0
      )
      {
         _task = task;
         _maxThreads = maxThreads;
         _actionTitle = actionTitle;
         _displayName = displayName;
         _totalCount = totalCount;
         _processed = initialProcessed;

         _sw = System.Diagnostics.Stopwatch.StartNew();

         UpdateDescription(0);
      }

      /// <summary>
      /// Gets an <see cref="IProgress{T}"/> instance that updates the UI.
      /// </summary>
      public IProgress<bool> GetProgressReporter()
      {
         return new Progress<bool>(HandleProgress);
      }

      /// <summary>
      /// Finalizes the progress task with a final rate calculation.
      /// </summary>
      public void FinalizeProgress()
      {
         var finalElapsed = _sw.Elapsed.TotalSeconds;
         var finalRate = _processed / (finalElapsed > 0 ? finalElapsed : 1);

         UpdateDescription(finalRate);

         _task.Value = _totalCount;
         _task.StopTask();
      }

      private void HandleProgress(bool success)
      {
         var currentProcessed = Interlocked.Increment(ref _processed);

         if (!success)
            Interlocked.Increment(ref _failedCount);

         lock (_progressLock)
         {
            var now = DateTime.Now;

            if (now - _lastUpdate < TimeSpan.FromSeconds(1) &&
                currentProcessed < _totalCount)
               return;

            _lastUpdate = now;
            _task.Value = currentProcessed;

            var swElapsed = _sw.Elapsed.TotalSeconds;
            var recsPerSec = currentProcessed / (swElapsed > 0 ? swElapsed : 1);

            UpdateDescription(recsPerSec);
         }
      }

      private void UpdateDescription(double rate)
      {
         _task.Description = GetDesc(
            _processed,
            _totalCount,
            rate,
            _failedCount,
            _maxThreads,
            _actionTitle,
            _displayName
         );
      }

      private static string GetDesc(
         int p,
         long t,
         double r,
         int f,
         int maxThreads,
         string actionTitle,
         string displayName
      )
      {
         var titleMarkup = $"{SystemConstants.UiMarkup.BoldRed}{actionTitle} " +
            $"{displayName}[/]";

         var rateInfo = r > 0 ? $" - {r:F1} r/s" : "";
         var desc = $"{titleMarkup} ({p}/{t}) " +
            $"[[{SystemConstants.UiMarkup.Green}{maxThreads}t{rateInfo}[/]]] ";

         if (f > 0)
            desc += $"{SystemConstants.UiMarkup.Red}({f} failed)[/]";

         return desc;
      }
   }
}
