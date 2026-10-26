using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing.Inlines;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering;

internal static class InlineImageRenderer
{
    public static Inline Render(
        ImageInline image,
        EditorTheme theme,
        IImageResolver? imageResolver,
        Action? requestLayoutRefresh)
    {
        var placeholder = new Run(string.IsNullOrEmpty(image.Alt) ? string.Empty : $"[{image.Alt}]")
        {
            Foreground = EditorBrushCache.Get(theme.LinkColor),
        };

        if (imageResolver is not null)
        {
            _ = placeholder.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _ = LoadInlineImageAsync(placeholder, image, imageResolver, requestLayoutRefresh)));
        }

        return placeholder;
    }

    private static async Task LoadInlineImageAsync(
        Run placeholder,
        ImageInline image,
        IImageResolver resolver,
        Action? requestLayoutRefresh)
    {
        try
        {
            var imageData = await Task.Run(() => resolver.ResolveImageAsync(image.Url, CancellationToken.None)).ConfigureAwait(false);
            if (imageData is null)
            {
                await ReplaceWithBrokenImageAsync(placeholder, image.Url, requestLayoutRefresh).ConfigureAwait(false);
                return;
            }

            if (ImageElementFactory.IsSvg(imageData))
            {
                await placeholder.Dispatcher.InvokeAsync(() =>
                {
                    var svg = ImageElementFactory.CreateSvgBrowser(
                        imageData,
                        image.Alt,
                        300,
                        image.DisplayWidth,
                        image.DisplayHeight);
                    if (svg is not null)
                    {
                        svg.InvalidateMeasure();
                        svg.InvalidateArrange();
                        ReplaceInline(placeholder, new InlineUIContainer(svg) { BaselineAlignment = BaselineAlignment.Bottom });
                        requestLayoutRefresh?.Invoke();
                    }
                    else
                    {
                        ReplaceInline(placeholder, CreateBrokenInline(image.Url));
                        requestLayoutRefresh?.Invoke();
                    }
                });
                return;
            }

            var bitmap = ImageElementFactory.CreateBitmap(imageData);
            if (bitmap is null)
            {
                await ReplaceWithBrokenImageAsync(placeholder, image.Url, requestLayoutRefresh).ConfigureAwait(false);
                return;
            }

            await placeholder.Dispatcher.InvokeAsync(() =>
            {
                var imageControl = ImageElementFactory.CreateBitmapImageControl(
                    bitmap,
                    image.Alt,
                    300,
                    alignLeft: false,
                    image.DisplayWidth,
                    image.DisplayHeight);
                imageControl.InvalidateMeasure();
                imageControl.InvalidateArrange();
                ReplaceInline(placeholder, new InlineUIContainer(imageControl) { BaselineAlignment = BaselineAlignment.Bottom });
                requestLayoutRefresh?.Invoke();
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            await ReplaceWithBrokenImageAsync(placeholder, image.Url, requestLayoutRefresh).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceWithBrokenImageAsync(Run placeholder, string url, Action? requestLayoutRefresh)
    {
        try
        {
            await placeholder.Dispatcher.InvokeAsync(() =>
            {
                ReplaceInline(placeholder, CreateBrokenInline(url));
                requestLayoutRefresh?.Invoke();
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // The document may have been detached while an async image request was completing.
        }
    }

    private static InlineUIContainer CreateBrokenInline(string url) =>
        new(ImageElementFactory.CreateBrokenImageIcon(url)) { BaselineAlignment = BaselineAlignment.Bottom };

    private static void ReplaceInline(Run placeholder, InlineUIContainer replacement)
    {
        switch (placeholder.Parent)
        {
            case Paragraph paragraph:
                ReplaceInline(paragraph.Inlines, placeholder, replacement);
                break;
            case Span span:
                ReplaceInline(span.Inlines, placeholder, replacement);
                break;
        }
    }

    private static void ReplaceInline(InlineCollection inlines, Run placeholder, InlineUIContainer replacement)
    {
        var next = placeholder.NextInline;
        inlines.Remove(placeholder);

        if (next is not null)
            inlines.InsertBefore(next, replacement);
        else
            inlines.Add(replacement);
    }
}
