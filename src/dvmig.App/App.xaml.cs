using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using dvmig.App.ViewModels;
using dvmig.App.Services;
using dvmig.Core;
using Serilog;

namespace dvmig.App
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();

            // Infrastructure
            services.AddSingleton<ILogger>(Log.Logger);
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IMigrationService, MigrationService>();

            // Core Migration Logic
            services.AddTransient<IUserMapper>(provider => 
            {
                var migrationService = provider.GetRequiredService<IMigrationService>();
                if (migrationService.SourceProvider == null || migrationService.TargetProvider == null)
                {
                    // Fallback or empty implementation if providers aren't ready
                    // In real use, we'd ensure they are ready before resolving
                }
                return new UserMapper(
                    migrationService.SourceProvider!, 
                    migrationService.TargetProvider!, 
                    provider.GetRequiredService<ILogger>());
            });

            services.AddTransient<IDataPreservationManager>(provider =>
            {
                var migrationService = provider.GetRequiredService<IMigrationService>();
                return new DataPreservationManager(
                    migrationService.TargetProvider!,
                    provider.GetRequiredService<ILogger>());
            });

            services.AddTransient<ISyncEngine>(provider =>
            {
                var migrationService = provider.GetRequiredService<IMigrationService>();
                return new SyncEngine(
                    migrationService.SourceProvider!,
                    migrationService.TargetProvider!,
                    provider.GetRequiredService<IUserMapper>(),
                    provider.GetRequiredService<IDataPreservationManager>(),
                    provider.GetRequiredService<ILogger>());
            });

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<ConnectionViewModel>();
            services.AddTransient<EntitySelectionViewModel>();
            services.AddTransient<MigrationDashboardViewModel>();

            // Navigation Factory
            services.AddSingleton<Func<Type, ViewModelBase>>(provider => 
                type => (ViewModelBase)provider.GetRequiredService(type));

            _serviceProvider = services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = new MainWindow();
            mainWindow.DataContext = _serviceProvider.GetRequiredService<MainViewModel>();
            mainWindow.Show();
            base.OnStartup(e);
        }
    }
}
