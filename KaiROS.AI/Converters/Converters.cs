using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using KaiROS.AI.Models;

namespace KaiROS.AI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool boolValue = value is bool b && b;

        if (invert) boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

public class CategoryToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLower() switch
        {
            "small" => "#10B981",
            "medium" => "#F59E0B",
            "large" => "#EF4444",
            _ => "#6B7280"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DownloadStateToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            DownloadState.NotStarted => "⬇",
            DownloadState.Downloading => "⏸",
            DownloadState.Paused => "▶",
            DownloadState.Completed => "✓",
            DownloadState.Failed => "✕",
            DownloadState.Verifying => "⏳",
            _ => "?"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BackendToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ExecutionBackend.Cpu => "CPU",
            ExecutionBackend.Cuda => "CUDA (NVIDIA)",
            ExecutionBackend.DirectML => "DirectML (AMD/Intel)",
            ExecutionBackend.Npu => "NPU",
            ExecutionBackend.Auto => "Auto-detect",
            _ => "Unknown"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value?.ToString());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ProgressToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double progress && parameter is double maxWidth)
        {
            return (progress / 100.0) * maxWidth;
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ProgressToWidthMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 3 &&
            values[0] is double value &&
            values[1] is double maximum &&
            values[2] is double width &&
            maximum > 0)
        {
            return (value / maximum) * width;
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class UrlToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                // Handle both pack:// URIs (local resources) and HTTP URLs
                if (url.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                {
                    bitmap.UriSource = new Uri(url, UriKind.Absolute);
                }
                else
                {
                    bitmap.UriSource = new Uri(url, UriKind.Absolute);
                }

                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 48; // Optimize for display size
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

