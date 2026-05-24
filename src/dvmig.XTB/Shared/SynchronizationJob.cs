using System;
using System.Collections.Generic;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;

using dvmig.Core.Interfaces;
using dvmig.Core.Synchronization;

namespace dvmig.XTB.Shared
{
   public class SynchronizationJob
   {
      private readonly IServiceProvider _serviceProvider;
      private readonly IDataverseProvider _sourceProvider;
      private readonly IDataverseProvider _targetProvider;
      private readonly IUserService _userService;
      private readonly List<string> _entityLogicalNames;
      private readonly int _totalRecords;
      private readonly bool _forceResync;
      private readonly Action<int, string> _reportProgress;
      private readonly Action<IReadOnlyCollection<UserMappingSummary>>
         _reportUserMappings;
      private readonly ILogger _logger;
      private TimeSpan _syncElapsed = TimeSpan.Zero;

      public TimeSpan SyncElapsed => _syncElapsed;

      public SynchronizationJob(
         IServiceProvider serviceProvider,
         IDataverseProvider sourceProvider,
         IDataverseProvider targetProvider,
         IUserService userService,
         List<string> entityLogicalNames,
         int totalRecords,
         bool forceResync,
         Action<int, string> reportProgress,
         Action<IReadOnlyCollection<UserMappingSummary>> reportUserMappings
      )
      {
         _serviceProvider = serviceProvider;
         _sourceProvider = sourceProvider;
         _targetProvider = targetProvider;
         _userService = userService;
         _entityLogicalNames = entityLogicalNames;
         _totalRecords = totalRecords;
         _forceResync = forceResync;
         _reportProgress = reportProgress;
         _reportUserMappings = reportUserMappings;
         _logger = _serviceProvider.GetRequiredService<ILogger>();
      }

      public void Run(CancellationToken ct)
      {
         var envService =
            _serviceProvider.GetRequiredService<IEnvironmentService>();
         var syncStateService =
            _serviceProvider.GetRequiredService<ISyncStateService>();
         var settings =
            _serviceProvider.GetRequiredService<ISettingsService>()
               .LoadSettings();

         // NOTE: EntityService is manually instantiated here,
         // as its constructor requires IDataverseProvider instance which
         // is runtime-dependent.
         var entityService = new EntityService(
            _logger,
            _targetProvider
         );

         _logger.Information("Validating target environment...");
         var isReady = envService
                        .ValidateTargetEnvironmentAsync(_targetProvider, ct)
                        .GetAwaiter()
                        .GetResult();

         if(!isReady)
         {
            _logger.Warning("Target environment is not ready. " +
                              "Installing required components...");

            envService.InstallComponentsAsync(_targetProvider, ct)
               .GetAwaiter()
               .GetResult();

            _logger.Information("Components installed successfully.");
         }

         var syncEngine = new SyncEngine(
             _sourceProvider,
             _targetProvider,
             _userService,
             _logger,
             entityService,
             syncStateService
         );

         syncEngine.InitializeSyncAsync(ct).GetAwaiter().GetResult();
         _reportUserMappings(
            _userService.GetMappingSummaryAsync(ct).GetAwaiter().GetResult()
         );

         var syncTimer = SyncTimer.StartNew();

         try
         {
            var options = new SyncOptions
            {
               MaxDegreeOfParallelism = settings.MaxParallelism,
               ForceResync = _forceResync,
               PreserveAuditData = true,
               StripMissingDependencies = true,
               AutoCreateRelatedRecords = settings.AutoCreateRelatedRecords
            };

            int processedRecords = 0;
            var progressStats = new SyncProgressStats(_totalRecords);

            foreach(var entityLogicalName in _entityLogicalNames)
            {
               ct.ThrowIfCancellationRequested();

               _logger.Information($"Starting synchronization for entity:" +
                                    $" {entityLogicalName}");

               _reportProgress(
                  GetProgressPercent(processedRecords),
                  FormatProgressMessage(
                     entityLogicalName,
                     processedRecords,
                     progressStats
                  )
               );

               var progress = new Progress<bool>(success =>
               {
                  var currentProcessedRecords =
                     Interlocked.Increment(ref processedRecords);
                  progressStats.RecordProgress();

                  _reportProgress(
                     GetProgressPercent(currentProcessedRecords),
                     FormatProgressMessage(
                        entityLogicalName,
                        currentProcessedRecords,
                        progressStats
                     )
                  );
               });

               syncEngine.SyncAsync(
                   entityLogicalName,
                   options,
                   null,
                   progress,
                   ct
               ).GetAwaiter().GetResult();

               _logger.Information($"Synchronization for entity " +
                                    $"{entityLogicalName} completed.");
            }
         }
         finally
         {
            _syncElapsed = syncTimer.Stop();
         }
      }

      private int GetProgressPercent(int processedRecords)
      {
         return _totalRecords > 0
            ? Math.Min((int)((double)processedRecords / _totalRecords * 100), 100)
            : 100;
      }

      private string FormatProgressMessage(
         string entityLogicalName,
         int processedRecords,
         SyncProgressStats progressStats
      )
      {
         var averageRate = progressStats.GetAverageRate(processedRecords);
         var currentRate = progressStats.GetCurrentRate();
         var eta = progressStats.GetEstimatedRemaining(processedRecords);

         return $"Synchronizing {entityLogicalName}... " +
            $"({processedRecords}/{_totalRecords}) | " +
            $"Avg: {FormatRate(averageRate)} r/s | " +
            $"Current: {FormatRate(currentRate)} r/s | " +
            $"ETA: {FormatDuration(eta)}";
      }

      private static string FormatRate(double? recordsPerSecond)
      {
         return recordsPerSecond.HasValue
            ? recordsPerSecond.Value.ToString("0.0")
            : "--";
      }

      private static string FormatDuration(TimeSpan? duration)
      {
         if (!duration.HasValue)
            return "--";

         return duration.Value.TotalHours >= 1
            ? duration.Value.ToString(@"h\:mm\:ss")
            : duration.Value.ToString(@"m\:ss");
      }

      private sealed class SyncProgressStats
      {
         private readonly object _syncRoot = new object();
         private readonly int _totalRecords;
         private readonly Queue<DateTime> _recentRecords =
            new Queue<DateTime>();
         private readonly DateTime _startedAt = DateTime.UtcNow;
         private static readonly TimeSpan _currentWindow =
            TimeSpan.FromSeconds(5);

         public SyncProgressStats(int totalRecords)
         {
            _totalRecords = totalRecords;
         }

         public void RecordProgress()
         {
            var now = DateTime.UtcNow;

            lock (_syncRoot)
            {
               _recentRecords.Enqueue(now);

               while (_recentRecords.Count > 0 &&
                      now - _recentRecords.Peek() > _currentWindow)
               {
                  _recentRecords.Dequeue();
               }
            }
         }

         public double? GetAverageRate(int processedRecords)
         {
            var elapsedSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds;

            return processedRecords > 0 && elapsedSeconds > 0
               ? processedRecords / elapsedSeconds
               : null;
         }

         public double? GetCurrentRate()
         {
            lock (_syncRoot)
            {
               if (_recentRecords.Count < 2)
                  return null;

               var elapsedSeconds =
                  (DateTime.UtcNow - _recentRecords.Peek()).TotalSeconds;

               return elapsedSeconds > 0
                  ? _recentRecords.Count / elapsedSeconds
                  : null;
            }
         }

         public TimeSpan? GetEstimatedRemaining(int processedRecords)
         {
            var averageRate = GetAverageRate(processedRecords);

            if (!averageRate.HasValue || averageRate.Value <= 0)
               return null;

            var remainingRecords = Math.Max(0, _totalRecords - processedRecords);

            return TimeSpan.FromSeconds(remainingRecords / averageRate.Value);
         }
      }
   }
}
