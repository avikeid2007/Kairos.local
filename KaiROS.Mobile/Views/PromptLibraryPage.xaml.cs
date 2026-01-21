using KaiROS.Mobile.ViewModels;

namespace KaiROS.Mobile.Views;

public partial class PromptLibraryPage : ContentPage
{
    private readonly PromptLibraryViewModel _viewModel;

    public PromptLibraryPage(PromptLibraryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadPresetsCommand.ExecuteAsync(null);
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void OnIconTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is string icon)
        {
            _viewModel.EditIcon = icon;
        }
    }
}
