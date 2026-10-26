using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Core.Parsing.Html;
using WpfMarkdownEditor.Core.Parsing.Inlines;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

public sealed class ParagraphRenderer(
    EditorTheme theme,
    IImageResolver? imageResolver = null,
    Action? requestLayoutRefresh = null) : IBlockRenderer
{
    private readonly InlineRenderer _inlineRenderer = new(theme, imageResolver, requestLayoutRefresh);
    private double ImageSpacingBottom => Math.Max(theme.ParagraphSpacing, 20);

    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var para = (ParagraphBlock)block;

        if (imageResolver is not null && TryGetStandaloneImageLines(para.Inlines, out var imageLines))
            return RenderStandaloneImageLines(imageLines);

        var paragraph = new Paragraph
        {
            FontFamily = theme.BodyFont,
            Foreground = EditorBrushCache.Get(theme.ForegroundColor),
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(0, 0, 0, para.Inlines.Any(static inline => inline is ImageInline) ? ImageSpacingBottom : theme.ParagraphSpacing),
        };

        _inlineRenderer.RenderInlines(paragraph, para.Inlines);
        return paragraph;
    }

    private System.Windows.Documents.Block RenderStandaloneImageLines(IReadOnlyList<StandaloneImageLine> imageLines)
    {
        if (imageLines.Count == 1)
            return RenderStandaloneImageLine(imageLines[0]);

        var section = new Section
        {
            Margin = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };

        foreach (var imageLine in imageLines)
            section.Blocks.Add(RenderStandaloneImageLine(imageLine));

        return section;
    }

    private System.Windows.Documents.Block RenderStandaloneImageLine(StandaloneImageLine imageLine) =>
        imageLine.Inline switch
        {
            ImageInline image => RenderStandaloneImage(image),
            HtmlInline html => RenderStandaloneLinkedHtmlImage(html),
            _ => throw new InvalidOperationException("Unsupported standalone image inline.")
        };

    private Paragraph RenderStandaloneLinkedHtmlImage(HtmlInline html)
    {
        var paragraph = new Paragraph
        {
            FontFamily = theme.BodyFont,
            Foreground = EditorBrushCache.Get(theme.ForegroundColor),
            TextAlignment = TextAlignment.Left,
            Margin = CreateImageMargin(),
        };

        _inlineRenderer.RenderInlines(paragraph, [html]);
        return paragraph;
    }

    private System.Windows.Documents.Block RenderStandaloneImage(ImageInline img)
    {
        // Fast synchronous path: local file via URI (no byte-array copy, fast I/O)
        var bitmap = TryLoadLocalBitmap(img.Url);
        if (bitmap is not null)
            return CreateImageContainer(bitmap, img);

        // Async path: data URIs and remote URLs via resolver
        var host = ImageElementFactory.CreateImageHost(BuildPlaceholder(img));
        var container = CreateImageBlockContainer(host);
        _ = LoadBlockImageAsync(host, img, imageResolver!, requestLayoutRefresh);
        return container;
    }

    private static BitmapImage? TryLoadLocalBitmap(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var fullPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, url));

            if (!System.IO.File.Exists(fullPath)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private BlockUIContainer CreateImageContainer(BitmapImage bitmap, ImageInline img)
    {
        var imageControl = ImageElementFactory.CreateBitmapImageControl(
            bitmap,
            img.Alt,
            400,
            alignLeft: true,
            img.DisplayWidth,
            img.DisplayHeight);
        return CreateImageBlockContainer(imageControl);
    }

    private FrameworkElement BuildPlaceholder(ImageInline img)
    {
        if (string.IsNullOrEmpty(img.Alt))
            return ImageElementFactory.CreateLoadingImagePlaceholder(img.Url);

        return new TextBlock
        {
            Text = $"[{img.Alt}]",
            Foreground = EditorBrushCache.Get(theme.LinkColor),
            FontStyle = FontStyles.Italic,
        };
    }

    private static async Task LoadBlockImageAsync(
        ContentControl host,
        ImageInline img,
        IImageResolver resolver,
        Action? requestLayoutRefresh)
    {
        try
        {
            var imageData = await resolver.ResolveImageAsync(img.Url, CancellationToken.None).ConfigureAwait(false);
            if (imageData is null)
            {
                await ReplaceWithBrokenImageAsync(host, img.Url, requestLayoutRefresh).ConfigureAwait(false);
                return;
            }

            if (ImageElementFactory.IsSvg(imageData))
            {
                await host.Dispatcher.InvokeAsync(() =>
                {
                    var svg = ImageElementFactory.CreateSvgBrowser(
                        imageData,
                        img.Alt,
                        400,
                        img.DisplayWidth,
                        img.DisplayHeight);
                    if (svg is not null)
                    {
                        RefreshLoadedImage(host, svg, requestLayoutRefresh);
                    }
                    else
                    {
                        ImageElementFactory.SetImageHostContent(host, ImageElementFactory.CreateBrokenImageIcon(img.Url));
                        requestLayoutRefresh?.Invoke();
                    }
                });
                return;
            }

            var bitmap = await Task.Run(() => ImageElementFactory.CreateBitmap(imageData)).ConfigureAwait(false);
            if (bitmap is null)
            {
                await ReplaceWithBrokenImageAsync(host, img.Url, requestLayoutRefresh).ConfigureAwait(false);
                return;
            }

            await host.Dispatcher.InvokeAsync(() =>
            {
                var imageControl = ImageElementFactory.CreateBitmapImageControl(
                    bitmap,
                    img.Alt,
                    400,
                    alignLeft: true,
                    img.DisplayWidth,
                    img.DisplayHeight);
                RefreshLoadedImage(host, imageControl, requestLayoutRefresh);
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            await ReplaceWithBrokenImageAsync(host, img.Url, requestLayoutRefresh).ConfigureAwait(false);
        }
    }

    private Thickness CreateImageMargin() => new(0, 8, 0, ImageSpacingBottom);

    private BlockUIContainer CreateImageBlockContainer(UIElement child) => new(child)
    {
        Margin = CreateImageMargin(),
        TextAlignment = TextAlignment.Left,
    };

    private static bool TryGetStandaloneImageLines(IReadOnlyList<Core.Parsing.Inline> inlines, out List<StandaloneImageLine> imageLines)
    {
        imageLines = [];
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case ImageInline image:
                    imageLines.Add(new StandaloneImageLine(image));
                    break;
                case HtmlInline html when IsStandaloneLinkedHtmlImage(html):
                    imageLines.Add(new StandaloneImageLine(html));
                    break;
                case LineBreakInline:
                    break;
                case TextInline text when string.IsNullOrWhiteSpace(text.Content):
                    break;
                default:
                    imageLines.Clear();
                    return false;
            }
        }

        return imageLines.Count > 0;
    }

    private static bool IsStandaloneLinkedHtmlImage(HtmlInline html) =>
        html.Fragment.Children is [HtmlElementNode { TagName: "a" } anchor] &&
        anchor.Children.Any(static child => child is HtmlElementNode { TagName: "img" }) &&
        anchor.Children.All(IsImageOrWhitespace);

    private static bool IsImageOrWhitespace(HtmlNode node) =>
        node switch
        {
            HtmlElementNode { TagName: "img" } => true,
            HtmlTextNode text => string.IsNullOrWhiteSpace(text.Text),
            _ => false
        };

    private sealed record StandaloneImageLine(Core.Parsing.Inline Inline);

    private static void RefreshLoadedImage(ContentControl host, FrameworkElement element, Action? requestLayoutRefresh)
    {
        ImageElementFactory.SetImageHostContent(host, element);
        element.InvalidateMeasure();
        element.InvalidateArrange();
        element.UpdateLayout();
        requestLayoutRefresh?.Invoke();
    }

    private static async Task ReplaceWithBrokenImageAsync(
        ContentControl host,
        string url,
        Action? requestLayoutRefresh)
    {
        try
        {
            await host.Dispatcher.InvokeAsync(() =>
            {
                ImageElementFactory.SetImageHostContent(host, ImageElementFactory.CreateBrokenImageIcon(url));
                requestLayoutRefresh?.Invoke();
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // The document may have been detached while an async image request was completing.
        }
    }

}
