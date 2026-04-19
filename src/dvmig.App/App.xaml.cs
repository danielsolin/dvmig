using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using dvmig.App.ViewModels;
using dvmig.App.Services;

namespace dvmig.App
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IMigrationService, MigrationService>();

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
