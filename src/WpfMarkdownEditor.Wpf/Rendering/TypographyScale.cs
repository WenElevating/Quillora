using System.Windows;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// Shared typography scale for Markdown and HTML rendering. Keeps heading sizes,
/// line heights and block rhythm in one place so renderers cannot drift apart.
/// Values follow Typora's GitHub theme (2.25 / 1.75 / 1.5 / 1.25 / 1 / 1 em).
/// </summary>
internal static class TypographyScale
{
    private static readonly double[] HeadingScales = [2.25, 1.75, 1.5, 1.25, 1, 1];

    private static readonly double[] HeadingLineHeightRatios = [1.2, 1.225, 1.43, 1.3, 1.6, 1.6];

    public const double BlockSpacingEm = 0.8;
    public const double HeadingTopSpacingEm = 0.2;

    public static double HeadingFontSize(EditorTheme theme, int level) =>
        theme.BaseFontSize * HeadingScales[ClampLevel(level)];

    public static double HeadingLineHeight(EditorTheme theme, int level) =>
        HeadingFontSize(theme, level) * HeadingLineHeightRatios[ClampLevel(level)];

    public static double BlockSpacing(EditorTheme theme) =>
        theme.BaseFontSize * BlockSpacingEm;

    public static Thickness ParagraphMargin(EditorTheme theme) =>
        new(0, 0, 0, BlockSpacing(theme));

    public static Thickness HeadingMargin(EditorTheme theme) =>
        new(0, theme.BaseFontSize * HeadingTopSpacingEm, 0, BlockSpacing(theme));

    public static double ResolveBodyLineHeight(EditorTheme theme) =>
        !double.IsNaN(theme.LineHeight) && theme.LineHeight > 0
            ? theme.LineHeight
            : theme.BaseFontSize * theme.LineHeightMultiplier;

    private static int ClampLevel(int level) => Math.Clamp(level, 1, 6) - 1;
}
