using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

/// <summary>
/// Renders image blocks. Shows a placeholder initially; loads images asynchronously
/// to avoid deadlocking the UI thread.
/// </summary>
public sealed class ImageRenderer(
    EditorTheme theme,
    IImageResolver? imageResolver = null,
    Action? requestLayoutRefresh = null) : IBlockRenderer
{
    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var image = (ImageBlock)block;
        var container = new BlockUIContainer
        {
            Margin = new Thickness(0, 8, 0, Math.Max(theme.ParagraphSpacing, 20)),
            TextAlignment = TextAlignment.Left,
        };

        var host = ImageElementFactory.CreateImageHost(BuildPlaceholder(image));
        container.Child = host;

        if (imageResolver is not null)
            _ = LoadImageAsync(host, image, imageResolver, requestLayoutRefresh);

        return container;
    }

    private FrameworkElement BuildPlaceholder(ImageBlock image)
    {
        if (string.IsNullOrEmpty(image.Alt))
            return ImageElementFactory.CreateLoadingImagePlaceholder(image.Url);

        return new TextBlock
        {
            Text = $"[{image.Alt}]",
            Foreground = EditorBrushCache.Get(theme.LinkColor),
            FontStyle = FontStyles.Italic,
        };
    }

    private static async Task LoadImageAsync(
        ContentControl host,
        ImageBlock image,
        IImageResolver resolver,
        Action? requestLayoutRefresh)
    {
        try
        {
            var imageData = await Task.Run(() => resolver.ResolveImageAsync(image.Url, CancellationToken.None)).ConfigureAwait(false);
            if (imageData is null)
            {
                await ReplaceWithBrokenImageAsync(host, image.Url, requestLayoutRefresh).ConfigureAwait(false);
                return;
            }

            if (ImageElementFactory.IsSvg(imageData))
            {
                await host.Dispatcher.InvokeAsync(() =>
                {
                    var svg = ImageElementFactory.CreateSvgBrowser(imageData, image.Alt, 400);
                    if (svg is not null)
                    {
                        RefreshLoadedImage(host, svg, requestLayoutRefresh);
                    }
                    else
                    {
                        ImageElementFactory.SetImageHostContent(host, ImageElementFactory.CreateBrokenImageIcon(image.Url));
                        requestLayoutRefresh?.Invoke();
                    }
                });
                return;
            }

            var bitmap = ImageElementFactory.CreateBitmap(imageData);
            if (bitmap is null)
            {
                await ReplaceWithBrokenImageAsync(host, image.Url, requestLayoutRefresh).ConfigureAwait(false);
                return;
            }

            await host.Dispatcher.InvokeAsync(() =>
            {
                var imageControl = ImageElementFactory.CreateBitmapImageControl(bitmap, image.Alt, 400, alignLeft: true);
                RefreshLoadedImage(host, imageControl, requestLayoutRefresh);
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            await ReplaceWithBrokenImageAsync(host, image.Url, requestLayoutRefresh).ConfigureAwait(false);
        }
    }

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
