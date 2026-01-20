using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaiROS.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _systemPrompt = "You are a helpful AI assistant. Be concise and accurate in your responses.";

    [ObservableProperty]
    private string _deviceModel = string.Empty;

    [ObservableProperty]
    private string _osVersion = string.Empty;

    [ObservableProperty]
    private string _totalMemory = string.Empty;

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    public SettingsViewModel()
    {
        LoadHardwareInfo();
    }

    private void LoadHardwareInfo()
    {
        DeviceModel = DeviceInfo.Model;
        OsVersion = $"Android {DeviceInfo.VersionString}";
        
        // Get available memory
        var totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        TotalMemory = $"{totalRam / (1024 * 1024 * 1024.0):F1} GB";
        
        // Get app version
        AppVersion = AppInfo.VersionString;
    }

    [RelayCommand]
    private async Task SaveSystemPrompt()
    {
        // Save to preferences
        Preferences.Set("SystemPrompt", SystemPrompt);
        await Shell.Current.DisplayAlert("Saved", "System prompt saved successfully!", "OK");
    }

    [RelayCommand]
    private async Task OpenFeedback()
    {
        try
        {
            var uri = new Uri("mailto:feedback@kairos.ai?subject=KaiROS%20Mobile%20Feedback");
            await Browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch
        {
            await Shell.Current.DisplayAlert("Error", "Could not open email client", "OK");
        }
    }

    [RelayCommand]
    private async Task OpenGitHub()
    {
        try
        {
            var uri = new Uri("https://github.com/avikeid2007/Kairos.local");
            await Browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch
        {
            await Shell.Current.DisplayAlert("Error", "Could not open browser", "OK");
        }
    }

    public void LoadSavedSettings()
    {
        SystemPrompt = Preferences.Get("SystemPrompt", SystemPrompt);
    }
}
