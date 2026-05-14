using System;
using System.Collections.Generic;

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
      private readonly Action<int, string> _reportProgress;
      private readonly ILogger _logger;

      public SynchronizationJob(
         IServiceProvider serviceProvider,
         IDataverseProvider sourceProvider,
         IDataverseProvider targetProvider,
         IUserService userService,
         List<string> entityLogicalNames,
         int totalRecords,
         Action<int, string> reportProgress
      )
      {
         _serviceProvider = serviceProvider;
         _sourceProvider = sourceProvider;
         _targetProvider = targetProvider;
         _userService = userService;
         _entityLogicalNames = entityLogicalNames;
         _totalRecords = totalRecords;
         _reportProgress = reportProgress;
         _logger = _serviceProvider.GetRequiredService<ILogger>();
      }

      public void Run()
      {
         var envService =
            _serviceProvider.GetRequiredService<IEnvironmentService>();
         var syncStateService =
            _serviceProvider.GetRequiredService<ISyncStateService>();

         // NOTE: EntityService is manually instantiated here,
         // as its constructor requires IDataverseProvider instance which
         // is runtime-dependent.
         var entityService = new EntityService(
            _logger,
            _targetProvider
         );

         _logger.Information("Validating target environment...");
         var isReady = envService
                        .ValidateTargetEnvironmentAsync(_targetProvider)
                        .GetAwaiter()
                        .GetResult();

         if(!isReady)
         {
            _logger.Warning("Target environment is not ready. " +
                              "Installing required components...");

            envService.InstallComponentsAsync(_targetProvider)
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

         syncEngine.InitializeSyncAsync().GetAwaiter().GetResult();

         var options = new SyncOptions
         {
            MaxDegreeOfParallelism = 4,
            ForceResync = false,
            PreserveAuditData = true,
            StripMissingDependencies = true
         };

         int processedRecords = 0;
         var progress = new Progress<bool>(success =>
         {
            processedRecords++;
            int percent = _totalRecords > 0
                   ? (int)((double)processedRecords / _totalRecords * 100)
                   : 100;

            _reportProgress(
                   Math.Min(percent, 100),
                   $"Synchronizing... ({processedRecords}/{_totalRecords})"
               );
         });

         foreach(var entityLogicalName in _entityLogicalNames)
         {
            _logger.Information($"Starting synchronization for entity:" +
                                 $" {entityLogicalName}");

            syncEngine.SyncAsync(
                entityLogicalName,
                options,
                null,
                progress
            ).GetAwaiter().GetResult();

            _logger.Information($"Synchronization for entity " +
                                 $"{entityLogicalName} completed.");
         }
      }
   }
}
