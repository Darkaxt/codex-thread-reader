using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace CodexThreadReader;

using MarkdownBlock = Markdig.Syntax.Block;
using MarkdownInline = Markdig.Syntax.Inlines.Inline;

public static class MarkdownFlowDocumentRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static FlowDocument Render(string text, ThemePalette palette, bool enableMarkdown)
    {
        return enableMarkdown
            ? RenderMarkdown(text, palette)
            : RenderPlainText(text, palette);
    }

    private static FlowDocument RenderMarkdown(string text, ThemePalette palette)
    {
        var document = CreateDocument(palette);
        var markdown = Markdown.Parse(text ?? string.Empty, Pipeline);
        foreach (var block in markdown)
        {
            AddBlock(document.Blocks, block, palette);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }

        return document;
    }

    private static FlowDocument RenderPlainText(string text, ThemePalette palette)
    {
        var document = CreateDocument(palette);
        document.Blocks.Add(new Paragraph(new Run(text ?? string.Empty))
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            Foreground = palette.Text,
            Margin = new Thickness(0)
        });
        return document;
    }

    private static FlowDocument CreateDocument(ThemePalette palette)
    {
        return new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = 760,
            Background = Brushes.Transparent,
            Foreground = palette.Text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            LineHeight = 19
        };
    }

    private static void AddBlock(BlockCollection blocks, MarkdownBlock block, ThemePalette palette)
    {
        switch (block)
        {
            case HeadingBlock heading:
                blocks.Add(CreateHeading(heading, palette));
                break;
            case ParagraphBlock paragraph:
                blocks.Add(CreateParagraph(paragraph, palette));
                break;
            case FencedCodeBlock fencedCode:
                blocks.Add(CreateCodeBlock(fencedCode, palette));
                break;
            case CodeBlock code:
                blocks.Add(CreateCodeBlock(code, palette));
                break;
            case ListBlock list:
                blocks.Add(CreateList(list, palette));
                break;
            case QuoteBlock quote:
                blocks.Add(CreateQuote(quote, palette));
                break;
            case ThematicBreakBlock:
                blocks.Add(new Paragraph(new Run("-----"))
                {
                    Foreground = palette.SubtleText,
                    Margin = new Thickness(0, 8, 0, 8)
                });
                break;
            case HtmlBlock html:
                blocks.Add(CreateCodeBlock(html, palette));
                break;
            default:
                var fallback = block.ToString();
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    blocks.Add(new Paragraph(new Run(fallback))
                    {
                        Foreground = palette.Text,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }
                break;
        }
    }

    private static Paragraph CreateHeading(HeadingBlock heading, ThemePalette palette)
    {
        var paragraph = new Paragraph
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = heading.Level switch
            {
                1 => 20,
                2 => 17,
                3 => 15,
                _ => 13.5
            },
            Foreground = palette.Text,
            Margin = new Thickness(0, 0, 0, 9)
        };
        AddInlines(paragraph.Inlines, heading.Inline, palette);
        return paragraph;
    }

    private static Paragraph CreateParagraph(ParagraphBlock paragraphBlock, ThemePalette palette)
    {
        var paragraph = new Paragraph
        {
            Foreground = palette.Text,
            Margin = new Thickness(0, 0, 0, 9)
        };
        AddInlines(paragraph.Inlines, paragraphBlock.Inline, palette);
        return paragraph;
    }

    private static Paragraph CreateCodeBlock(LeafBlock codeBlock, ThemePalette palette)
    {
        return new Paragraph(new Run(codeBlock.Lines.ToString()))
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            Background = palette.ChatTool,
            Foreground = palette.Text,
            Margin = new Thickness(0, 3, 0, 10)
        };
    }

    private static List CreateList(ListBlock listBlock, ThemePalette palette)
    {
        var list = new List
        {
            MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(20, 0, 0, 8),
            Foreground = palette.Text
        };

        foreach (var item in listBlock.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            foreach (var child in item)
            {
                AddBlock(listItem.Blocks, child, palette);
            }

            list.ListItems.Add(listItem);
        }

        return list;
    }

    private static Section CreateQuote(QuoteBlock quoteBlock, ThemePalette palette)
    {
        var section = new Section
        {
            Background = palette.ChatTool,
            Foreground = palette.Text,
            Margin = new Thickness(8, 2, 0, 10)
        };
        foreach (var child in quoteBlock)
        {
            AddBlock(section.Blocks, child, palette);
        }

        return section;
    }

    private static void AddInlines(InlineCollection inlines, ContainerInline? container, ThemePalette palette)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            AddInline(inlines, inline, palette);
        }
    }

    private static void AddInline(InlineCollection inlines, MarkdownInline inline, ThemePalette palette)
    {
        switch (inline)
        {
            case LiteralInline literal:
                inlines.Add(new Run(literal.Content.ToString()));
                break;
            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;
            case CodeInline code:
                inlines.Add(new Run(code.Content)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = palette.ChatTool,
                    Foreground = palette.Text
                });
                break;
            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    span.FontStyle = FontStyles.Italic;
                }

                AddInlines(span.Inlines, emphasis, palette);
                inlines.Add(span);
                break;
            case LinkInline link:
                var hyperlink = new Hyperlink
                {
                    Foreground = palette.AccentText
                };
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
                {
                    hyperlink.NavigateUri = uri;
                    hyperlink.RequestNavigate += OpenHyperlink;
                }

                AddInlines(hyperlink.Inlines, link, palette);
                if (!hyperlink.Inlines.Any())
                {
                    hyperlink.Inlines.Add(new Run(link.Url ?? string.Empty));
                }

                inlines.Add(hyperlink);
                break;
            case ContainerInline nested:
                AddInlines(inlines, nested, palette);
                break;
            default:
                var fallback = inline.ToString();
                if (!string.IsNullOrEmpty(fallback))
                {
                    inlines.Add(new Run(fallback));
                }
                break;
        }
    }

    private static void OpenHyperlink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
