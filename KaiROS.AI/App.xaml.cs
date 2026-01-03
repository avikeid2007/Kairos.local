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
        
        // Load saved theme preference at startup
        LoadSavedTheme();
        
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
    
    private void LoadSavedTheme()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var themePath = Path.Combine(localAppData, "KaiROS.AI", "theme.txt");
            
            if (File.Exists(themePath))
            {
                var savedTheme = File.ReadAllText(themePath).Trim();
                if (savedTheme == "Light")
                {
                    // Replace the dark theme with light theme
                    var lightTheme = new System.Windows.ResourceDictionary
                    {
                        Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                    };
                    
                    // Find and remove dark theme
                    System.Windows.ResourceDictionary? themeToRemove = null;
                    foreach (var dict in Resources.MergedDictionaries)
                    {
                        var source = dict.Source?.OriginalString ?? "";
                        if (source.Contains("ModernTheme.xaml"))
                        {
                            themeToRemove = dict;
                            break;
                        }
                    }
                    
                    if (themeToRemove != null)
                    {
                        Resources.MergedDictionaries.Remove(themeToRemove);
                    }
                    
                    Resources.MergedDictionaries.Insert(0, lightTheme);
                }
            }
        }
        catch { /* Ignore errors, use default dark theme */ }
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
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IDownloadService>(sp => new DownloadService(modelsDir));
        services.AddSingleton<IHardwareDetectionService, HardwareDetectionService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ModelManagerService>();
        services.AddSingleton<IModelManagerService>(sp => sp.GetRequiredService<ModelManagerService>());
        services.AddSingleton<ChatService>();
        services.AddSingleton<IChatService>(sp => sp.GetRequiredService<ChatService>());
        services.AddSingleton<IApiService, ApiService>();
        
        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ModelCatalogViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DocumentViewModel>();
        
        // Views
        services.AddSingleton<MainWindow>();
    }
    
    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
