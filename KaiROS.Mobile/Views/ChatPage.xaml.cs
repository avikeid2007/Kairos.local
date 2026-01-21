using KaiROS.Mobile.ViewModels;

namespace KaiROS.Mobile.Views;

public partial class ChatPage : ContentPage
{
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        if (BindingContext is ChatViewModel vm)
        {
            vm.UpdateModelStatus();
            _ = vm.InitializeAsync();
        }
    }
}
