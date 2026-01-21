namespace KaiROS.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(ViewModels.SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.SettingsViewModel vm)
        {
            vm.LoadSavedSettings();
        }
    }

    private async void OnPromptLibraryTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("PromptLibrary");
    }
}
