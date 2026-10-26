using System.Windows;
using System.Windows.Media;

namespace WpfMarkdownEditor.Wpf.Theming;

/// <summary>
/// Shared typography for the source editor and the live preview.
/// </summary>
/// <remarks>
/// The families are defined in <c>Theming/EditorFonts.xaml</c> and loaded through the XAML
/// parser on purpose: a pack URI contains commas (<c>application:,,,</c>), so
/// <c>new FontFamily("pack://..., Noto Sans SC, ...")</c> in C# is split at the wrong place and
/// silently falls back to the last plain family. The XAML converter handles the URI form correctly.
/// Loading is retried until the WPF application is available, then cached.
/// </remarks>
public static class EditorFonts
{
    private const string DictionaryUri =
        "pack://application:,,,/WpfMarkdownEditor.Wpf;component/Theming/EditorFonts.xaml";

    private static readonly object Sync = new();
    private static readonly FontFamily SegoeFallback = new("Segoe UI");
    private static readonly FontFamily ConsolasFallback = new("Consolas");

    private static FontFamily? _body;
    private static FontFamily? _code;

    /// <summary>Body / heading font: Open Sans for Latin, Noto Sans SC for CJK, YaHei UI as last resort.</summary>
    public static FontFamily Body
    {
        get
        {
            lock (Sync)
            {
                _body ??= Load("EditorBodyFont");
            }

            return _body ?? SegoeFallback;
        }
    }

    /// <summary>Monospace font for code blocks and inline code.</summary>
    public static FontFamily Code
    {
        get
        {
            lock (Sync)
            {
                _code ??= Load("EditorCodeFont");
            }

            return _code ?? ConsolasFallback;
        }
    }

    private static FontFamily? Load(string key)
    {
        try
        {
            var dictionary = new ResourceDictionary { Source = new Uri(DictionaryUri, UriKind.Absolute) };
            return dictionary[key] as FontFamily;
        }
        catch (Exception)
        {
            // Resource dictionary unavailable (e.g. before the WPF application exists) - retry later.
            return null;
        }
    }
}
