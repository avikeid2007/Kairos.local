using System.IO;
using System.Windows;
using KaiROS.AI.Models;
using Microsoft.Win32;
using WpfMessageBox = System.Windows.MessageBox;

namespace KaiROS.AI.Views;

public partial class AddCustomModelDialog : Window
{
    public CustomModelEntity? Result { get; private set; }
    
    public AddCustomModelDialog()
    {
        InitializeComponent();
        
        // Toggle panels based on radio button
        LocalFileRadio.Checked += (s, e) => 
        {
            LocalFilePanel.Visibility = Visibility.Visible;
            DownloadUrlPanel.Visibility = Visibility.Collapsed;
        };
        DownloadUrlRadio.Checked += (s, e) => 
        {
            LocalFilePanel.Visibility = Visibility.Collapsed;
            DownloadUrlPanel.Visibility = Visibility.Visible;
        };
    }
    
    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "GGUF Model Files (*.gguf)|*.gguf|All Files (*.*)|*.*",
            Title = "Select Model File"
        };
        
        if (dialog.ShowDialog() == true)
        {
            FilePathBox.Text = dialog.FileName;
            
            // Auto-fill display name if empty
            if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
            {
                DisplayNameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }
    
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    private void AddModel_Click(object sender, RoutedEventArgs e)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
        {
            WpfMessageBox.Show("Please enter a display name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        bool isLocal = LocalFileRadio.IsChecked == true;
        
        if (isLocal && string.IsNullOrWhiteSpace(FilePathBox.Text))
        {
            WpfMessageBox.Show("Please select a model file.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (!isLocal && string.IsNullOrWhiteSpace(DownloadUrlBox.Text))
        {
            WpfMessageBox.Show("Please enter a download URL.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (isLocal && !File.Exists(FilePathBox.Text))
        {
            WpfMessageBox.Show("The selected file does not exist.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        // Create result
        var fileName = isLocal 
            ? Path.GetFileName(FilePathBox.Text) 
            : Path.GetFileName(new Uri(DownloadUrlBox.Text).LocalPath);
        
        long fileSize = 0;
        if (isLocal && File.Exists(FilePathBox.Text))
        {
            fileSize = new FileInfo(FilePathBox.Text).Length;
        }
        
        Result = new CustomModelEntity
        {
            Name = fileName,
            DisplayName = DisplayNameBox.Text.Trim(),
            Description = DescriptionBox.Text?.Trim() ?? string.Empty,
            FilePath = isLocal ? FilePathBox.Text : string.Empty,
            DownloadUrl = isLocal ? string.Empty : DownloadUrlBox.Text.Trim(),
            SizeBytes = fileSize,
            IsLocal = isLocal,
            AddedDate = DateTime.UtcNow
        };
        
        DialogResult = true;
        Close();
    }
}
