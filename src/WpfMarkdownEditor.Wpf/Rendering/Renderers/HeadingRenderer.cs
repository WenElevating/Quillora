using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

public sealed class HeadingRenderer(
    EditorTheme theme,
    IImageResolver? imageResolver = null,
    Action? requestLayoutRefresh = null) : IBlockRenderer
{
    private readonly InlineRenderer _inlineRenderer = new(theme, imageResolver, requestLayoutRefresh);

    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var heading = (HeadingBlock)block;
        var paragraph = new Paragraph
        {
            FontFamily = theme.HeadingFont,
            Foreground = EditorBrushCache.Get(theme.HeadingColor),
            FontSize = GetFontSize(heading.Level),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, theme.HeadingMarginTop, 0, theme.HeadingMarginBottom),
        };

        _inlineRenderer.RenderInlines(paragraph, heading.Inlines);

        // GitHub-style: add bottom border for h1 and h2
        if (theme.ShowHeadingBorders && heading.Level <= 2)
        {
            var section = new Section
            {
                BorderBrush = EditorBrushCache.Get(theme.HeadingBorderColor),
                BorderThickness = new Thickness(0, 0, 0, heading.Level == 1 ? 1 : 1),
                Padding = new Thickness(0, 0, 0, 8),
            };
            section.Blocks.Add(paragraph);
            return section;
        }

        return paragraph;
    }

    private double GetFontSize(int level) => level switch
    {
        1 => Math.Round(theme.BaseFontSize * 1.85),
        2 => Math.Round(theme.BaseFontSize * 1.5),
        3 => Math.Round(theme.BaseFontSize * 1.25),
        4 => Math.Round(theme.BaseFontSize * 1.1),
        5 => theme.BaseFontSize,
        6 => theme.BaseFontSize,
        _ => theme.BaseFontSize,
    };
}
