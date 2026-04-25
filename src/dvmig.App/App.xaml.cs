using System.Windows;
using dvmig.App.Services;
using dvmig.App.ViewModels;
using dvmig.Core.DataPreservation;
using dvmig.Core.Logging;
using dvmig.Core.Provisioning;
using dvmig.Core.Settings;
using dvmig.Core.Synchronization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace dvmig.App
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            Log.Logger = LoggerInitializer.Initialize("dvmig.App");

            var services = new ServiceCollection();

            // Infrastructure
            services.AddSingleton<ILogger>(Log.Logger);
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IMigrationService, MigrationService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<
                IEnvironmentValidator,
                EnvironmentValidator
            >();
            services.AddSingleton<ISchemaManager, SchemaManager>();
            services.AddSingleton<IPluginDeployer, PluginDeployer>();
            services.AddSingleton<ISetupService, SetupService>();
            services.AddSingleton<ISyncStateTracker, LocalFileStateTracker>();

            // Core Migration Logic
            services.AddTransient<IUserMapper>(provider =>
            {
                var migrationService = provider
                    .GetRequiredService<IMigrationService>();

                return new UserMapper(
                    migrationService.SourceProvider!,
                    migrationService.TargetProvider!,
                    provider.GetRequiredService<ILogger>()
                );
            });

            services.AddTransient<IDataPreservationManager>(provider =>
            {
                var migrationService = provider
                    .GetRequiredService<IMigrationService>();

                return new DataPreservationManager(
                    migrationService.TargetProvider!,
                    provider.GetRequiredService<ILogger>()
                );
            });

            services.AddTransient<ISyncEngine>(provider =>
            {
                var migrationService = provider
                    .GetRequiredService<IMigrationService>();

                return new SyncEngine(
                    migrationService.SourceProvider!,
                    migrationService.TargetProvider!,
                    provider.GetRequiredService<IUserMapper>(),
                    provider.GetRequiredService<IDataPreservationManager>(),
                    provider.GetRequiredService<ISyncStateTracker>(),
                    provider.GetRequiredService<ILogger>()
                );
            });

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<ConnectionViewModel>();
            services.AddTransient<EntitySelectionViewModel>();
            services.AddTransient<MigrationDashboardViewModel>();

            // Navigation Factory
            services.AddSingleton<Func<Type, ViewModelBase>>(provider =>
            {
                return type => (ViewModelBase)provider.GetRequiredService(type);
            });

            _serviceProvider = services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = new MainWindow();

            mainWindow.DataContext = _serviceProvider
                .GetRequiredService<MainViewModel>();

            mainWindow.Show();

            base.OnStartup(e);
        }
    }
}
