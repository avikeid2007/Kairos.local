using KaiROS.Mobile.ViewModels;

namespace KaiROS.Mobile.Views;

public partial class ChatHistoryPage : ContentPage
{
    private readonly ChatHistoryViewModel _viewModel;

    public ChatHistoryPage(ChatHistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadSessionsCommand.ExecuteAsync(null);
    }
}
