using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.Mobile.Models;
using KaiROS.Mobile.Services;
using System.Collections.ObjectModel;

namespace KaiROS.Mobile.ViewModels;

public partial class PromptLibraryViewModel : ObservableObject
{
    private readonly PromptLibraryService _promptService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private SystemPromptPreset? _selectedPreset;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editPrompt = string.Empty;

    [ObservableProperty]
    private string _editIcon = "🤖";

    public ObservableCollection<SystemPromptPreset> Presets { get; } = new();

    public string[] AvailableIcons { get; } = new[] 
    { 
        "🤖", "✍️", "💻", "📋", "🌐", "🎨", "📚", "🔬", "💡", "🎯", 
        "🧠", "📝", "🔧", "🎭", "🌟", "🚀", "💬", "🎓", "🏆", "⚡" 
    };

    public PromptLibraryViewModel(PromptLibraryService promptService)
    {
        _promptService = promptService;
    }

    [RelayCommand]
    private async Task LoadPresetsAsync()
    {
        try
        {
            IsLoading = true;
            var presets = await _promptService.GetAllPresetsAsync();
            
            Presets.Clear();
            foreach (var preset in presets)
            {
                Presets.Add(preset);
            }
            
            // Mark active preset
            var activeId = _promptService.ActivePreset?.Id;
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == activeId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectPresetAsync(SystemPromptPreset preset)
    {
        await _promptService.SetActivePresetAsync(preset.Id);
        SelectedPreset = preset;
        await Shell.Current.DisplayAlert("Preset Activated", $"Now using: {preset.Name}", "OK");
    }

    [RelayCommand]
    private void StartNewPreset()
    {
        EditName = "";
        EditPrompt = "";
        EditIcon = "🤖";
        SelectedPreset = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditPreset(SystemPromptPreset preset)
    {
        SelectedPreset = preset;
        EditName = preset.Name;
        EditPrompt = preset.PromptText;
        EditIcon = preset.Icon;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            await Shell.Current.DisplayAlert("Error", "Please enter a name", "OK");
            return;
        }

        var preset = SelectedPreset ?? new SystemPromptPreset();
        preset.Name = EditName.Trim();
        preset.PromptText = EditPrompt.Trim();
        preset.Icon = EditIcon;

        await _promptService.SavePresetAsync(preset);
        IsEditing = false;
        await LoadPresetsAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task DeletePresetAsync(SystemPromptPreset preset)
    {
        if (preset.IsDefault)
        {
            await Shell.Current.DisplayAlert("Cannot Delete", "Cannot delete the default preset", "OK");
            return;
        }

        var confirm = await Shell.Current.DisplayAlert(
            "Delete Preset",
            $"Delete \"{preset.Name}\"?",
            "Delete",
            "Cancel");

        if (!confirm) return;

        await _promptService.DeletePresetAsync(preset.Id);
        await LoadPresetsAsync();
    }
}
