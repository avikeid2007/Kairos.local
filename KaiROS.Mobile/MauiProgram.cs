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

        // Remove Entry underline on Android
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (h, v) =>
        {
#if ANDROID
            h.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
        });

        // Register Services (Singleton for shared state)
        builder.Services.AddSingleton<MobileLLMService>();
        builder.Services.AddSingleton<ModelDownloadService>();
        builder.Services.AddSingleton<ChatDatabaseService>();
        builder.Services.AddSingleton<PromptLibraryService>();
        builder.Services.AddSingleton<VoiceService>();
        builder.Services.AddSingleton<DocumentService>();

        // Register ViewModels
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<ModelSelectionViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ChatHistoryViewModel>();
        builder.Services.AddTransient<PromptLibraryViewModel>();

        // Register Pages
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<ModelSelectionPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ChatHistoryPage>();
        builder.Services.AddTransient<PromptLibraryPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
