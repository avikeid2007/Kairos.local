using System.Globalization;
using System.Windows.Data;
using KaiROS.AI.Controls;
using KaiROS.AI.Services;

namespace KaiROS.AI.Converters;

/// <summary>
/// Converts message content to a list of UI elements (TextBlocks and CodeBlocks)
/// </summary>
public class MarkdownContentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string content || string.IsNullOrEmpty(content))
            return new System.Windows.Controls.StackPanel();
        
        var panel = new System.Windows.Controls.StackPanel();
        var segments = MarkdownParser.Parse(content);
        
        foreach (var segment in segments)
        {
            if (segment.Type == SegmentType.CodeBlock)
            {
                var codeBlock = new CodeBlock
                {
                    Code = segment.Content,
                    CodeLanguage = segment.Language,
                    Margin = new System.Windows.Thickness(0, 4, 0, 4)
                };
                panel.Children.Add(codeBlock);
            }
            else
            {
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = segment.Content,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
                };
                panel.Children.Add(textBlock);
            }
        }
        
        return panel;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

