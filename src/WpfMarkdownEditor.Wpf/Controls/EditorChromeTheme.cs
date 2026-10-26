using System.Windows.Media;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Applies an <see cref="EditorTheme"/> to the editor chrome: source pane, preview host,
/// toolbar, zoom labels and context menu resources.
/// </summary>
internal static class EditorChromeTheme
{
    public static void Apply(MarkdownEditor editor, EditorTheme theme)
    {
        // Editor pane colors — use softer foreground for readability
        editor.EditorTextBox.Background = Frozen(theme.BackgroundColor);
        editor.EditorTextBox.Foreground = Frozen(theme.EditorForegroundColor);
        editor.EditorTextBox.CaretBrush = Frozen(theme.EditorCaretColor);
        editor.EditorTextBox.FontWeight = theme.EditorFontWeight;
        editor.EditorSplitter.Background = Frozen(theme.ThematicBreakColor);

        // Preview pane colors
        editor.PreviewViewer.Background = Frozen(theme.BackgroundColor);

        // Toolbar theme brushes
        var lum = 0.299 * theme.BackgroundColor.R + 0.587 * theme.BackgroundColor.G + 0.114 * theme.BackgroundColor.B;
        var isDark = lum < 128;
        editor.Resources["ToolbarBackground"] = Frozen(
            isDark ? Color.FromRgb(0x16, 0x1b, 0x22) : Color.FromRgb(0xf6, 0xf8, 0xfa));
        editor.Resources["ToolbarBorder"] = Frozen(
            isDark ? Color.FromRgb(0x30, 0x36, 0x3d) : Color.FromRgb(0xd0, 0xd7, 0xde));
        editor.Resources["ToolbarForeground"] = Frozen(
            isDark ? Color.FromRgb(0xe6, 0xed, 0xf3) : Color.FromRgb(0x24, 0x29, 0x2f));
        editor.Resources["ToolbarHoverBackground"] = Frozen(
            isDark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));
        editor.Resources["ToolbarPressedBackground"] = Frozen(
            isDark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x28, 0x00, 0x00, 0x00));
        editor.Resources["ToolbarAccent"] = Frozen(
            isDark ? Color.FromRgb(0x58, 0xa6, 0xff) : Color.FromRgb(0x09, 0x69, 0xda));

        // Toolbar border & separator
        editor.PreviewToolbarBorder.BorderBrush = Frozen(
            isDark ? Color.FromRgb(0x30, 0x36, 0x3d) : Color.FromRgb(0xd0, 0xd7, 0xde));
        editor.PreviewToolbarBorder.Background = Frozen(
            isDark ? Color.FromRgb(0x16, 0x1b, 0x22) : Color.FromRgb(0xf6, 0xf8, 0xfa));
        editor.ToolBarSeparator.Background = Frozen(
            isDark ? Color.FromRgb(0x30, 0x36, 0x3d) : Color.FromRgb(0xd0, 0xd7, 0xde));
        editor.ZoomText.Foreground = Frozen(
            isDark ? Color.FromRgb(0x8b, 0x94, 0x9e) : Color.FromRgb(0x65, 0x6d, 0x76));

        // Context menu theme
        editor.Resources["MenuBackground"] = Frozen(
            isDark ? Color.FromRgb(0x2d, 0x2d, 0x2d) : Color.FromRgb(0xff, 0xff, 0xff));
        editor.Resources["MenuBorder"] = Frozen(
            isDark ? Color.FromRgb(0x3d, 0x3d, 0x3d) : Color.FromRgb(0xe5, 0xe5, 0xe5));
        editor.Resources["MenuForeground"] = Frozen(
            isDark ? Color.FromRgb(0xff, 0xff, 0xff) : Color.FromRgb(0x1a, 0x1a, 0x1a));
        editor.Resources["MenuHoverBackground"] = Frozen(
            isDark ? Color.FromRgb(0x38, 0x38, 0x38) : Color.FromRgb(0xf5, 0xf5, 0xf5));
        editor.Resources["MenuSeparator"] = Frozen(
            isDark ? Color.FromRgb(0x3d, 0x3d, 0x3d) : Color.FromRgb(0xe5, 0xe5, 0xe5));
        editor.Resources["MenuDisabledForeground"] = Frozen(
            isDark ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0xb0, 0xb0, 0xb0));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
