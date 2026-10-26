using System.Collections.Concurrent;
using System.Windows.Media;

namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// Shares frozen brushes by color across the rendering layer. Theme colors are a small,
/// bounded set, so a process-wide cache avoids re-allocating the same brush for every
/// block and inline element during full renders and theme switches.
/// </summary>
internal static class EditorBrushCache
{
    private static readonly ConcurrentDictionary<Color, SolidColorBrush> Cache = new();

    public static SolidColorBrush Get(Color color) =>
        Cache.GetOrAdd(color, static value =>
        {
            var brush = new SolidColorBrush(value);
            brush.Freeze();
            return brush;
        });

    internal static int Count => Cache.Count;
}
