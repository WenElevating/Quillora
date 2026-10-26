using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

/// <summary>
/// Renders ListBlock with recursive nesting support and Typora-style marker rotation:
/// Level 1: ● Disc, Level 2: ○ Circle, Level 3: ■ Square, then repeats.
/// </summary>
public sealed class ListRenderer(
    EditorTheme theme,
    IImageResolver? imageResolver = null,
    Action? requestLayoutRefresh = null) : IBlockRenderer
{
    private readonly InlineRenderer _inlineRenderer = new(theme, imageResolver, requestLayoutRefresh);

    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        return RenderList((ListBlock)block, level: 0);
    }

    private List RenderList(ListBlock list, int level)
    {
        var markerStyle = list.IsOrdered
            ? TextMarkerStyle.Decimal
            : GetUnorderedMarkerStyle(level);

        var listEl = new List
        {
            MarkerStyle = markerStyle,
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(theme.ListIndent, 0, 0, 0),
        };

        // Smaller markers for nested lists
        if (level > 0)
            listEl.FontSize = theme.BaseFontSize * 0.85;

        foreach (var item in list.Items)
        {
            var listItem = new System.Windows.Documents.ListItem();
            foreach (var itemBlock in item.Blocks)
            {
                switch (itemBlock)
                {
                    case ParagraphBlock para:
                        var p = new Paragraph
                        {
                            FontFamily = theme.BodyFont,
                            Foreground = EditorBrushCache.Get(theme.ForegroundColor),
                            FontSize = theme.BaseFontSize,
                            Margin = new Thickness(0),
                        };
                        _inlineRenderer.RenderInlines(p, para.Inlines);
                        listItem.Blocks.Add(p);
                        break;
                    case ListBlock nestedList:
                        listItem.Blocks.Add(RenderList(nestedList, level + 1));
                        break;
                }
            }
            listEl.ListItems.Add(listItem);
        }

        return listEl;
    }

    /// <summary>
    /// Typora-style marker rotation: ● → ○ → ■ → repeats.
    /// </summary>
    private static TextMarkerStyle GetUnorderedMarkerStyle(int level) => (level % 3) switch
    {
        0 => TextMarkerStyle.Disc,   // ● filled circle
        1 => TextMarkerStyle.Circle, // ○ hollow circle
        2 => TextMarkerStyle.Square, // ■ filled square
        _ => TextMarkerStyle.Disc,
    };
}
