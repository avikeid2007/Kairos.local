using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using KaiROS.AI.ViewModels;

namespace KaiROS.AI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isExiting = false;
    
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        
        // Set window icon from file
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                
                // Also set tray icon
                TrayIcon.Icon = new Icon(iconPath);
            }
        }
        catch { /* Ignore icon loading errors */ }
        
        Loaded += async (s, e) =>
        {
            await viewModel.InitializeAsync();
        };
    }
    
    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Minimize to tray when minimized
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            TrayIcon.Visibility = Visibility.Visible;
        }
    }
    
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            Hide();
            TrayIcon.Visibility = Visibility.Visible;
            
            // Show notification first time
            TrayIcon.ShowBalloonTip("KaiROS AI", "App minimized to system tray. Right-click for options.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        else
        {
            // Actually close - dispose tray icon
            TrayIcon.Dispose();
        }
    }
    
    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        TrayIcon.Visibility = Visibility.Collapsed;
    }
    
    private void TrayMenu_NewChat(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
        _viewModel.NavigateToChatCommand.Execute(null);
        (_viewModel.CurrentView as ChatViewModel)?.NewSessionCommand.Execute(null);
    }
    
    private void TrayMenu_Settings(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
        _viewModel.NavigateToSettingsCommand.Execute(null);
    }
    
    private void TrayMenu_Restore(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
    }
    
    private void TrayMenu_Exit(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Close();
    }
}
