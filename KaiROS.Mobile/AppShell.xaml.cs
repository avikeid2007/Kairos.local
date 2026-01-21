using KaiROS.Mobile.Views;

namespace KaiROS.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));
        Routing.RegisterRoute(nameof(ModelSelectionPage), typeof(ModelSelectionPage));
        Routing.RegisterRoute("PromptLibrary", typeof(PromptLibraryPage));
    }
}
