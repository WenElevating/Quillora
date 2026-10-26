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
///
/// Font families are cached per thread because WPF font/resource objects are thread-affine;
/// accessing a shared family from several STA threads (tests, embedded editors) corrupts
/// WPF's internal caches. Loading is retried until the WPF application/resource system exists.
/// </remarks>
public static class EditorFonts
{
    private const string DictionaryUri =
        "pack://application:,,,/WpfMarkdownEditor.Wpf;component/Theming/EditorFonts.xaml";

    [ThreadStatic] private static FontFamily? _body;
    [ThreadStatic] private static FontFamily? _code;
    [ThreadStatic] private static FontFamily? _segoeFallback;
    [ThreadStatic] private static FontFamily? _consolasFallback;

    /// <summary>Body / heading font: Open Sans for Latin, Noto Sans SC for CJK, YaHei UI as last resort.</summary>
    public static FontFamily Body
    {
        get
        {
            if (_body is null)
            {
                var loaded = Load("EditorBodyFont");
                if (loaded is not null)
                    _body = loaded;
            }

            return _body ??= _segoeFallback ??= new FontFamily("Segoe UI");
        }
    }

    /// <summary>Monospace font for code blocks and inline code.</summary>
    public static FontFamily Code
    {
        get
        {
            if (_code is null)
            {
                var loaded = Load("EditorCodeFont");
                if (loaded is not null)
                    _code = loaded;
            }

            return _code ??= _consolasFallback ??= new FontFamily("Consolas");
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
