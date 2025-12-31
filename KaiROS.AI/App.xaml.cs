using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using KaiROS.AI.Services;
using KaiROS.AI.ViewModels;
using KaiROS.AI.Models;

namespace KaiROS.AI;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        
        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        _serviceProvider = services.BuildServiceProvider();
        
        // Create and show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
    
    private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.AddSingleton<IConfiguration>(configuration);
        
        // Get app settings - Use LocalAppData for MSIX compatibility (installation folder is read-only)
        var appSettings = configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var modelsDir = Path.Combine(localAppData, "KaiROS.AI", "Models");
        
        // Services
        services.AddSingleton<IDownloadService>(sp => new DownloadService(modelsDir));
        services.AddSingleton<IHardwareDetectionService, HardwareDetectionService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ModelManagerService>();
        services.AddSingleton<IModelManagerService>(sp => sp.GetRequiredService<ModelManagerService>());
        services.AddSingleton<ChatService>();
        services.AddSingleton<IChatService>(sp => sp.GetRequiredService<ChatService>());
        
        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ModelCatalogViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<SettingsViewModel>();
        
        // Views
        services.AddSingleton<MainWindow>();
    }
    
    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
