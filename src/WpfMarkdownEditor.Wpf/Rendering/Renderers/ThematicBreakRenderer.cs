using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

public sealed class ThematicBreakRenderer(EditorTheme theme) : IBlockRenderer
{
    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var line = new Border
        {
            Height = 2,
            Background = EditorBrushCache.Get(theme.ThematicBreakColor),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        return new BlockUIContainer(line)
        {
            Margin = new Thickness(0, 16, 0, 16),
        };
    }
}
