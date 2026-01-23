using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using KaiROS.AI.Controls;
using KaiROS.AI.Services;
using WpfBrush = System.Windows.Media.Brush;

namespace KaiROS.AI.Converters;

/// <summary>
/// Converts message content to a list of UI elements with markdown formatting
/// </summary>
public class MarkdownContentConverter : IValueConverter
{
    private static readonly Regex HeaderPattern = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex BlockquotePattern = new(@"^>\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BoldPattern = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicPattern = new(@"\*(.+?)\*", RegexOptions.Compiled);
    private static readonly Regex InlineCodePattern = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex ListItemPattern = new(@"^[\s]*[-*•]\s+(.+)$", RegexOptions.Compiled); // Removed Multiline to handle line-by-line

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string content || string.IsNullOrEmpty(content))
            return new StackPanel();

        var panel = new StackPanel();
        var segments = MarkdownParser.Parse(content);

        foreach (var segment in segments)
        {
            if (segment.Type == SegmentType.CodeBlock)
            {
                var codeBlock = new CodeBlock
                {
                    Code = segment.Content,
                    CodeLanguage = segment.Language,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                panel.Children.Add(codeBlock);
            }
            else
            {
                // Process text block line by line for block-level elements
                var lines = segment.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        // Add some spacing for empty lines, optional
                        if (panel.Children.Count > 0)
                            panel.Children.Add(new TextBlock { Height = 8 });
                        continue;
                    }

                    var headerMatch = HeaderPattern.Match(line);
                    if (headerMatch.Success)
                    {
                        var level = headerMatch.Groups[1].Length;
                        var text = headerMatch.Groups[2].Value;
                        panel.Children.Add(CreateHeader(text, level));
                        continue;
                    }

                    var quoteMatch = BlockquotePattern.Match(line);
                    if (quoteMatch.Success)
                    {
                        var text = quoteMatch.Groups[1].Value;
                        panel.Children.Add(CreateBlockquote(text));
                        continue;
                    }

                    var listMatch = ListItemPattern.Match(line);
                    if (listMatch.Success)
                    {
                        var text = listMatch.Groups[1].Value;
                        panel.Children.Add(CreateListItem(text));
                        continue;
                    }

                    // Standard Paragraph
                    panel.Children.Add(CreateFormattedTextBlock(line));
                }
            }
        }

        return panel;
    }

    private UIElement CreateHeader(string text, int level)
    {
        double fontSize = level switch
        {
            1 => 24,
            2 => 20,
            3 => 18,
            _ => 16
        };

        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = (WpfBrush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
            Margin = new Thickness(0, 12, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        return block;
    }

    private UIElement CreateBlockquote(string text)
    {
        var border = new Border
        {
            BorderBrush = (WpfBrush)System.Windows.Application.Current.Resources["PrimaryBrush"],
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 4, 0, 4),
            Margin = new Thickness(0, 4, 0, 4),
            Background = (WpfBrush)System.Windows.Application.Current.Resources["SurfaceLightBrush"]
        };

        var content = CreateFormattedTextBlock(text);
        if (content is TextBlock tb) tb.FontStyle = FontStyles.Italic;
        border.Child = content;

        return border;
    }

    private UIElement CreateListItem(string text)
    {
        var itemPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(8, 2, 0, 2) };
        itemPanel.Children.Add(new TextBlock
        {
            Text = "•  ",
            Foreground = (WpfBrush)System.Windows.Application.Current.Resources["AccentBrush"],
            FontWeight = FontWeights.Bold
        });
        itemPanel.Children.Add(CreateFormattedTextBlock(text));
        return itemPanel;
    }

    private TextBlock CreateFormattedTextBlock(string text)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
            LineHeight = 22 // Improve readability
        };

        // Find all matches: Bold, Code, Link
        var matches = new List<(int Index, int Length, string Text, string Type, string? Url)>();

        foreach (Match m in BoldPattern.Matches(text))
            matches.Add((m.Index, m.Length, m.Groups[1].Value, "bold", null));

        foreach (Match m in InlineCodePattern.Matches(text))
            matches.Add((m.Index, m.Length, m.Groups[1].Value, "code", null));

        foreach (Match m in LinkPattern.Matches(text))
            matches.Add((m.Index, m.Length, m.Groups[1].Value, "link", m.Groups[2].Value));

        matches = matches.OrderBy(m => m.Index).ToList();

        int currentIndex = 0;
        if (matches.Count == 0)
        {
            textBlock.Inlines.Add(new Run(text) { Foreground = (WpfBrush)System.Windows.Application.Current.Resources["TextPrimaryBrush"] });
        }
        else
        {
            foreach (var match in matches)
            {
                if (match.Index > currentIndex)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(currentIndex, match.Index - currentIndex))
                    {
                        Foreground = (WpfBrush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
                    });
                }

                if (match.Type == "bold")
                {
                    textBlock.Inlines.Add(new Run(match.Text)
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = (WpfBrush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
                    });
                }
                else if (match.Type == "code")
                {
                    textBlock.Inlines.Add(new Run(match.Text)
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        Background = (WpfBrush)System.Windows.Application.Current.Resources["SurfaceLightBrush"],
                        Foreground = (WpfBrush)System.Windows.Application.Current.Resources["AccentBrush"]
                    });
                }
                else if (match.Type == "link")
                {
                    var link = new Hyperlink(new Run(match.Text))
                    {
                        NavigateUri = new Uri(match.Url ?? ""),
                        Foreground = (WpfBrush)System.Windows.Application.Current.Resources["PrimaryLightBrush"]
                    };
                    // Handle navigation manually usually required in WPF or Bind command
                    link.RequestNavigate += (s, e) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        }
                        catch { }
                    };
                    textBlock.Inlines.Add(link);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                textBlock.Inlines.Add(new Run(text.Substring(currentIndex))
                {
                    Foreground = (WpfBrush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
                });
            }
        }

        return textBlock;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

