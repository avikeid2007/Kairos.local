using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Helpers;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Color = Microsoft.Maui.Graphics.Color;

namespace KaiROS.Mobile.Controls;

public class MarkdownView : VerticalStackLayout
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(MarkdownView), string.Empty,
        propertyChanged: OnTextChanged);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is MarkdownView view)
        {
            view.RenderMarkdown((string)newValue);
        }
    }

    private void RenderMarkdown(string markdown)
    {
        Children.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        try
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var document = Markdown.Parse(markdown, pipeline);

            foreach (var block in document)
            {
                var view = RenderBlock(block);
                if (view != null)
                {
                    Children.Add(view);
                }
            }
        }
        catch (Exception)
        {
            // Fallback for parsing errors
            Children.Add(new Label { Text = markdown, TextColor = Colors.White });
        }
    }

    private View? RenderBlock(Block block)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph),
            CodeBlock code => RenderCodeBlock(code),
            QuoteBlock quote => RenderQuote(quote),
            ListBlock list => RenderList(list),
            ThematicBreakBlock => new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2A2A50"), Margin = new Thickness(0, 8) },
            _ => null
        };
    }

    private View RenderHeading(HeadingBlock heading)
    {
        var fs = new FormattedString();
        AddInlinesToFormattedString(heading.Inline, fs);

        double fontSize = heading.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 18,
            _ => 16
        };

        return new Label
        {
            FormattedText = fs,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            Margin = new Thickness(0, 12, 0, 8)
        };
    }

    private View RenderParagraph(ParagraphBlock paragraph)
    {
        var fs = new FormattedString();
        AddInlinesToFormattedString(paragraph.Inline, fs);

        return new Label
        {
            FormattedText = fs,
            FontSize = 14,
            TextColor = Color.FromArgb("#E8E8F0"), // Slightly off-white for body
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private View RenderCodeBlock(CodeBlock codeBlock)
    {
        var code = string.Empty;
        if (codeBlock is FencedCodeBlock fenced)
        {
            // Fenced code block (```)
            var sb = new System.Text.StringBuilder();
            foreach (var line in fenced.Lines)
            {
                sb.AppendLine(line.ToString());
            }
            code = sb.ToString().TrimEnd();
        }
        else
        {
            // Indented code block
            var sb = new System.Text.StringBuilder();
            foreach (var line in codeBlock.Lines)
            {
                sb.AppendLine(line.ToString());
            }
            code = sb.ToString().TrimEnd();
        }

        // Create header with Copy button
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Padding = new Thickness(12, 8),
            BackgroundColor = Color.FromArgb("#2A2A50") // Darker header
        };

        var copyLabel = new Label
        {
            Text = "Copy",
            FontSize = 12,
            TextColor = Color.FromArgb("#B0B0D0"),
            VerticalOptions = LayoutOptions.Center
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += async (s, e) =>
        {
            await Clipboard.Default.SetTextAsync(code);
            copyLabel.Text = "Copied!";
            await Task.Delay(2000);
            copyLabel.Text = "Copy";
        };
        copyLabel.GestureRecognizers.Add(tapGesture);

        headerGrid.Add(copyLabel, 1);

        // Code content
        var scrollView = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = new Label
            {
                Text = code,
                FontFamily = "Monospace",
                FontSize = 13,
                TextColor = Color.FromArgb("#A6ACCD"),
                Margin = new Thickness(12)
            }
        };

        return new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = Color.FromArgb("#1E1E2E"),
            Stroke = Color.FromArgb("#2A2A50"),
            Margin = new Thickness(0, 8, 0, 12),
            Padding = new Thickness(0), // Build header inside
            Content = new VerticalStackLayout
            {
                Children =
                {
                    headerGrid,
                    scrollView
                }
            }
        };
    }

    private View RenderQuote(QuoteBlock quote)
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        foreach (var subBlock in quote)
        {
            var view = RenderBlock(subBlock);
            if (view != null) stack.Children.Add(view);
        }

        return new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            BackgroundColor = Color.FromArgb("#1E1E2E"), // Dark bg for quote
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Margin = new Thickness(0, 8, 0, 8),
            Padding = new Thickness(12, 8),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = 4 },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                Children =
                {
                    new BoxView { Color = Color.FromArgb("#7B2CBF"), WidthRequest = 4 }, // Purple accent line
                    new ContentView { Content = stack, Margin = new Thickness(12, 0, 0, 0) }.Column(1)
                }
            }
        };
    }

    private View RenderList(ListBlock list)
    {
        var stack = new VerticalStackLayout { Spacing = 4, Margin = new Thickness(0, 4, 0, 8) };
        bool isOrdered = list.IsOrdered;
        int index = 1;

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var itemStack = new VerticalStackLayout();
                foreach (var subBlock in listItem)
                {
                    var view = RenderBlock(subBlock);
                    if (view != null) itemStack.Children.Add(view);
                }

                var bullet = isOrdered ? $"{index++}." : "•";

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = BroadWidth(isOrdered) },
                        new ColumnDefinition { Width = GridLength.Star }
                    },
                    Margin = new Thickness(8, 0, 0, 0)
                };

                grid.Add(new Label
                {
                    Text = bullet,
                    TextColor = Color.FromArgb("#B0B0D0"),
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.End
                }, 0);

                grid.Add(new ContentView { Content = itemStack, Margin = new Thickness(8, 0, 0, 0) }, 1);

                stack.Children.Add(grid);
            }
        }

        return stack;
    }

    private GridLength BroadWidth(bool ordered) => ordered ? 25 : 15;

    private void AddInlinesToFormattedString(ContainerInline? inlines, FormattedString fs)
    {
        if (inlines == null) return;

        foreach (var inline in inlines)
        {
            if (inline is LiteralInline literal)
            {
                fs.Spans.Add(new Span
                {
                    Text = literal.Content.ToString(),
                    TextColor = Color.FromArgb("#E8E8F0")
                });
            }
            else if (inline is EmphasisInline emphasis)
            {
                // Recursive call for nested inlines (e.g. bold AND italic)
                var childFs = new FormattedString();
                AddInlinesToFormattedString(emphasis, childFs); // EmphasisInline is a ContainerInline

                foreach (var span in childFs.Spans)
                {
                    if (emphasis.DelimiterCount == 2)
                        span.FontAttributes = span.FontAttributes | FontAttributes.Bold;
                    else
                        span.FontAttributes = span.FontAttributes | FontAttributes.Italic;

                    fs.Spans.Add(span);
                }
            }
            else if (inline is CodeInline code)
            {
                fs.Spans.Add(new Span
                {
                    Text = code.Content,
                    FontFamily = "Monospace",
                    BackgroundColor = Color.FromArgb("#2A2A50"),
                    TextColor = Color.FromArgb("#F0F0FF")
                });
            }
            else if (inline is LinkInline link)
            {
                var url = link.Url;
                var childFs = new FormattedString();
                AddInlinesToFormattedString(link, childFs);

                foreach (var span in childFs.Spans)
                {
                    span.TextColor = Color.FromArgb("#3B82F6"); // Blue link
                    span.TextDecorations = TextDecorations.Underline;
                    if (!string.IsNullOrEmpty(url))
                    {
                        span.GestureRecognizers.Add(new TapGestureRecognizer
                        {
                            Command = new Command(async () => await Launcher.OpenAsync(url))
                        });
                    }
                    fs.Spans.Add(span);
                }
            }
            else if (inline is LineBreakInline)
            {
                fs.Spans.Add(new Span { Text = "\n" });
            }
        }
    }
}

public static class GridExtensions
{
    public static View Column(this View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }
}
