using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

public sealed class BlockquoteRenderer(EditorTheme theme, FlowDocumentRenderer parent) : IBlockRenderer
{
    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var bq = (BlockquoteBlock)block;
        var section = new Section
        {
            Background = EditorBrushCache.Get(theme.BlockquoteBackground),
            BorderBrush = EditorBrushCache.Get(theme.BlockquoteBorder),
            BorderThickness = new Thickness(theme.BlockquoteBorderWidth, 0, 0, 0),
            Padding = new Thickness(theme.BlockquotePaddingLeft, 8, 16, 8),
            Margin = new Thickness(0, 4, 0, 4),
        };

        foreach (var child in bq.Children)
        {
            var rendered = parent.RenderBlock(child);
            if (rendered is not null)
                section.Blocks.Add(rendered);
        }

        return section;
    }
}
