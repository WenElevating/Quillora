using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.SyntaxHighlighting;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

public sealed class CodeBlockRenderer(EditorTheme theme, SyntaxHighlighter? highlighter = null) : IBlockRenderer
{
    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var code = (CodeBlock)block;
        var fontSize = theme.BaseFontSize * theme.CodeFontScale;
        var paragraph = new Paragraph
        {
            FontFamily = theme.CodeFont,
            FontSize = fontSize,
            LineHeight = fontSize * 1.45,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = EditorBrushCache.Get(theme.CodeForeground),
            Padding = new Thickness(12, 8, 12, 6),
            Margin = new Thickness(0),
        };

        // Start with plain text. Syntax highlighting is applied by the viewport-aware
        // scheduler only for code blocks that are visible, because every colored
        // segment costs a WPF Run and large code-heavy documents would otherwise stall.
        paragraph.Inlines.Add(new Run(code.Code));

        if (highlighter is not null && !string.IsNullOrEmpty(code.Language))
        {
            var target = new CodeBlockTarget(paragraph, code.Code, code.Language, highlighter, theme);
            CodeBlockHighlightMetadata.Set(paragraph, target);
        }

        // Wrap in Section for background + border (GitHub-style)
        var section = new Section
        {
            Background = EditorBrushCache.Get(theme.CodeBackground),
            BorderBrush = EditorBrushCache.Get(theme.CodeBlockBorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, TypographyScale.BlockSpacing(theme)),
        };
        section.Blocks.Add(paragraph);
        return section;
    }
}
