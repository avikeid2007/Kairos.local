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

    private void MessageInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Check if Enter is pressed without Shift
        if (e.Key == System.Windows.Input.Key.Enter && !System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
        {
            if (DataContext is ViewModels.ChatViewModel vm && vm.IsEnterToSendEnabled)
            {
                e.Handled = true;
                if (vm.SendMessageCommand.CanExecute(null))
                {
                    vm.SendMessageCommand.Execute(null);
                }
            }
        }
    }
}
