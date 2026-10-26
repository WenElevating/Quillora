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
        var fontSize = TypographyScale.HeadingFontSize(theme, heading.Level);
        var paragraph = new Paragraph
        {
            FontFamily = theme.HeadingFont,
            Foreground = EditorBrushCache.Get(
                heading.Level == 6 ? theme.MutedHeadingColor : theme.HeadingColor),
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            LineHeight = TypographyScale.HeadingLineHeight(theme, heading.Level),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = TypographyScale.HeadingMargin(theme),
        };

        _inlineRenderer.RenderInlines(paragraph, heading.Inlines);

        // Typora/GitHub: h1 and h2 carry a thin bottom border.
        if (theme.ShowHeadingBorders && heading.Level <= 2)
        {
            var section = new Section
            {
                BorderBrush = EditorBrushCache.Get(theme.HeadingBorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 4),
                Margin = new Thickness(0),
            };
            section.Blocks.Add(paragraph);
            return section;
        }

        return paragraph;
    }
}
