namespace KaiROS.AI.Views;

public partial class ChatView : System.Windows.Controls.UserControl
{
    public ChatView()
    {
        InitializeComponent();
    }
    
    private void ExportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }
}
