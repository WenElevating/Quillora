using System.Windows;
using System.Windows.Controls;
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
        var allTasks = list.Items.Count > 0 && list.Items.All(static item => item.IsTask);
        var markerStyle = allTasks
            ? TextMarkerStyle.None
            : list.IsOrdered
                ? TextMarkerStyle.Decimal
                : GetUnorderedMarkerStyle(level);

        var listEl = new List
        {
            MarkerStyle = markerStyle,
            Margin = new Thickness(0, 0, 0, TypographyScale.BlockSpacing(theme)),
            Padding = new Thickness(allTasks ? 0 : theme.ListIndent, 0, 0, 0),
        };

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

                        if (item.IsTask)
                        {
                            var checkbox = new CheckBox
                            {
                                IsChecked = item.IsTaskChecked,
                                IsHitTestVisible = false,
                                Focusable = false,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 6, 0),
                            };
                            p.Inlines.Add(new InlineUIContainer(checkbox)
                            {
                                BaselineAlignment = BaselineAlignment.Center,
                            });
                        }

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
