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
         Action<int, string> reportProgress
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

            foreach(var entityLogicalName in _entityLogicalNames)
            {
               ct.ThrowIfCancellationRequested();

               _logger.Information($"Starting synchronization for entity:" +
                                    $" {entityLogicalName}");

               _reportProgress(
                  GetProgressPercent(processedRecords),
                  $"Synchronizing {entityLogicalName}... " +
                  $"({processedRecords}/{_totalRecords})"
               );

               var progress = new Progress<bool>(success =>
               {
                  processedRecords++;

                  _reportProgress(
                     GetProgressPercent(processedRecords),
                     $"Synchronizing {entityLogicalName}... " +
                     $"({processedRecords}/{_totalRecords})"
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
   }
}
