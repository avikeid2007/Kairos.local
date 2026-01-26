namespace KaiROS.AI.Views;

public partial class DocumentView : System.Windows.Controls.UserControl
{
    public DocumentView()
    {
        InitializeComponent();
    }

    private void OpenContextMenu_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }
}
