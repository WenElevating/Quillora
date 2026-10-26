using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

public sealed class ThematicBreakRenderer(EditorTheme theme) : IBlockRenderer
{
    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, 12, 0, 12),
        };
        var line = new Run(new string('\u2500', 80))
        {
            Foreground = EditorBrushCache.Get(theme.ThematicBreakColor),
            FontSize = 12,
        };
        para.Inlines.Add(line);
        return para;
    }
}
