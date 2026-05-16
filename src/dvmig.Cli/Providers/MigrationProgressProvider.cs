using System.Runtime.CompilerServices;
using dvmig.Core.Shared;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvmig.Cli.Providers
{
   /// <summary>
   /// Provides a bridge between the core migration progress and the 
   /// Spectre.Console UI, handling rate calculations and markup formatting.
   /// </summary>
   public class MigrationProgressProvider
   {
      internal static readonly ConditionalWeakTable<ProgressTask, MigrationProgressProvider> 
         Providers = new();

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
      /// Gets the number of failed records.
      /// </summary>
      public int FailedCount => _failedCount;

      /// <summary>
      /// Gets the maximum number of threads.
      /// </summary>
      public int MaxThreads => _maxThreads;

      /// <summary>
      /// Initializes a new instance of the 
      /// <see cref="MigrationProgressProvider"/> class.
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

         _task.Description = _displayName;
         Providers.Add(_task, this);
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
         }
      }
   }

   /// <summary>
   /// Renders the entity name column.
   /// </summary>
   public sealed class EntityColumn : ProgressColumn
   {
      /// <inheritdoc/>
      public override IRenderable Render(
         RenderOptions options,
         ProgressTask task,
         TimeSpan elapsed
      )
      {
         var text = task.Description;

         return new Markup($"{SystemConstants.UiMarkup.BoldRed}{text}[/]");
      }
   }

   /// <summary>
   /// Renders the record count column (processed/total).
   /// </summary>
   public sealed class RecordCountColumn : ProgressColumn
   {
      /// <inheritdoc/>
      public override IRenderable Render(
         RenderOptions options,
         ProgressTask task,
         TimeSpan elapsed
      )
      {
         var p = (long)task.Value;
         var t = (long)task.MaxValue;

         return new Markup($"({p}/{t})");
      }
   }

   /// <summary>
   /// Renders the speed and thread info column.
   /// </summary>
   public sealed class SpeedColumn : ProgressColumn
   {
      /// <inheritdoc/>
      public override IRenderable Render(
         RenderOptions options,
         ProgressTask task,
         TimeSpan elapsed
      )
      {
         if (MigrationProgressProvider.Providers.TryGetValue(
            task,
            out var provider
         ))
         {
            var r = task.Speed ?? 0;
            var rateInfo = r > 0 ? $" - {r:F1} r/s" : "";

            var desc = $"[[{SystemConstants.UiMarkup.Green}" +
               $"{provider.MaxThreads}t{rateInfo}[/]]] ";

            if (provider.FailedCount > 0)
               desc += $"{SystemConstants.UiMarkup.Red}" +
                  $"({provider.FailedCount} failed)[/]";

            return new Markup(desc);
         }

         return new Markup("");
      }
   }
}
