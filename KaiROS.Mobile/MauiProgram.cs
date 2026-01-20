using CommunityToolkit.Maui;
using KaiROS.Mobile.Services;
using KaiROS.Mobile.ViewModels;
using KaiROS.Mobile.Views;
using Microsoft.Extensions.Logging;

namespace KaiROS.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Services (Singleton for shared state)
        builder.Services.AddSingleton<MobileLLMService>();
        builder.Services.AddSingleton<ModelDownloadService>();

        // Register ViewModels
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<ModelSelectionViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Register Pages
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<ModelSelectionPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
