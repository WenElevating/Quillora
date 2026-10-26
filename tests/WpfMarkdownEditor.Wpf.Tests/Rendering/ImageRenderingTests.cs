using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Xml.Linq;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Inlines;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Services;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class ImageRenderingTests
{
    private static readonly byte[] Png1x1 =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static readonly byte[] SvgBadge = Encoding.UTF8.GetBytes(
        """<svg xmlns="http://www.w3.org/2000/svg" width="80" height="20"><rect width="80" height="20" fill="#d73a49"/></svg>""");

    private static readonly byte[] ForeignObjectSvgBadge = Encoding.UTF8.GetBytes(
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="250" height="55" viewBox="0 0 250 53">
          <rect stroke="#4a0e99" stroke-width="1" fill="#FFFFFF" x="0.5" y="0.5" width="249" height="53" rx="10"/>
          <foreignObject width="198" height="17" style="font-size: 9px;color: rgb(67, 39, 135);font-family: Arial;font-weight: 400;text-align: center;line-height: 1.5;" x="6" y="10">
            <div xmlns="http://www.w3.org/1999/xhtml">GITHUB TRENDING</div>
          </foreignObject>
          <foreignObject width="230" height="35" style="font-size: 14px;color: rgb(67, 39, 135);font-family: Arial;font-weight: 700;text-align: left;line-height: 1.5;" x="64" y="24">
            <div xmlns="http://www.w3.org/1999/xhtml">#1 Repository Of The Day</div>
          </foreignObject>
        </svg>
        """);

    [Fact]
    public void Render_LocalImagePathWithSpaces_CreatesImageControl()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "folder with spaces");
            var imagePath = Path.Combine(imageDirectory, "pasted image.png");
            Directory.CreateDirectory(imageDirectory);
            File.WriteAllBytes(imagePath, Png1x1);

            try
            {
                var parser = new MarkdownParser();
                using var imageLoader = new ImageLoader();
                var renderer = new FlowDocumentRenderer(EditorTheme.Light, imageLoader);

                var document = renderer.Render(parser.Parse($"![pasted]({imagePath})"));
                var block = Assert.IsType<BlockUIContainer>(Assert.Single(document.Blocks.Cast<WpfBlock>()));
                var image = Assert.IsAssignableFrom<Image>(block.Child);

                Assert.NotNull(image.Source);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void RenderInline_RemoteImage_LoadsAsynchronously()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new InlineRenderer(EditorTheme.Light, resolver);
            var paragraph = new Paragraph();

            renderer.RenderInlines(paragraph,
            [
                new ImageInline
                {
                    Url = "https://example.com/image.png",
                    Alt = "remote"
                }
            ]);

            var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
            Assert.Equal("[remote]", run.Text);

            Assert.True(WaitUntil(() => resolver.ResolveStarted.Task.IsCompleted));
            Assert.Equal("https://example.com/image.png", resolver.ResolveStarted.Task.Result);

            resolver.Complete(Png1x1);

            Assert.True(WaitUntil(() => paragraph.Inlines.Single() is InlineUIContainer));
            var container = Assert.IsType<InlineUIContainer>(Assert.Single(paragraph.Inlines));
            var image = Assert.IsAssignableFrom<Image>(container.Child);
            Assert.NotNull(image.Source);
        });
    }

    [Fact]
    public void RenderInline_RemoteSvgImage_CreatesSvgBrowser()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new InlineRenderer(EditorTheme.Light, resolver);
            var paragraph = new Paragraph();

            renderer.RenderInlines(paragraph,
            [
                new ImageInline
                {
                    Url = "https://example.com/badge.svg",
                    Alt = "badge"
                }
            ]);

            Assert.True(WaitUntil(() => resolver.ResolveStarted.Task.IsCompleted));
            resolver.Complete(SvgBadge, "svg");

            Assert.True(WaitUntil(() => paragraph.Inlines.Single() is InlineUIContainer container && container.Child is WebBrowser));
            var imageContainer = Assert.IsType<InlineUIContainer>(Assert.Single(paragraph.Inlines));
            var browser = Assert.IsType<WebBrowser>(imageContainer.Child);
            Assert.Equal(80, browser.Width);
            Assert.Equal(20, browser.Height);
        });
    }

    [Fact]
    public void RenderInline_RemoteImages_AddsReadableInlineSpacing()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new InlineRenderer(EditorTheme.Light, resolver);
            var paragraph = new Paragraph();

            renderer.RenderInlines(paragraph,
            [
                new ImageInline { Url = "https://img.shields.io/badge/Quick_Start-blue", Alt = "Quick Start" },
                new TextInline { Content = " " },
                new ImageInline { Url = "https://img.shields.io/badge/License-MIT-yellow", Alt = "License" }
            ]);

            Assert.True(WaitUntil(() => resolver.ResolveStarted.Task.IsCompleted));
            resolver.Complete(Png1x1);

            Assert.True(WaitUntil(() => CountInlines<InlineUIContainer>(paragraph.Inlines.Cast<System.Windows.Documents.Inline>()) == 2));
            var elements = FlattenInlines(paragraph.Inlines.Cast<System.Windows.Documents.Inline>())
                .OfType<InlineUIContainer>()
                .Select(static container => Assert.IsAssignableFrom<FrameworkElement>(container.Child))
                .ToList();

            Assert.Equal(2, elements.Count);
            Assert.All(elements, element => Assert.True(element.Margin.Right >= 4));
        });
    }

    [Fact]
    public void NormalizeSvgForBrowser_ForeignObjectText_ConvertsToSvgText()
    {
        var svg = ImageElementFactory.NormalizeSvgForBrowser(ForeignObjectSvgBadge);

        Assert.DoesNotContain("foreignObject", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<text", svg);
        Assert.Contains("GITHUB TRENDING", svg);
        Assert.Contains("#1 Repository Of The Day", svg);

        var label = XDocument.Parse(svg)
            .Descendants()
            .Single(element => element.Name.LocalName == "text" && element.Value == "#1 Repository Of The Day");
        Assert.Equal("35.9", label.Attribute("y")?.Value);
    }

    [Fact]
    public void RenderInline_ImageLoadFailure_ReplacesUrlPlaceholderWithBrokenIcon()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new InlineRenderer(EditorTheme.Light, resolver);
            var paragraph = new Paragraph();

            renderer.RenderInlines(paragraph,
            [
                new ImageInline
                {
                    Url = "https://camo.githubusercontent.com/image",
                }
            ]);

            Assert.True(WaitUntil(() => resolver.ResolveStarted.Task.IsCompleted));
            resolver.Complete(null);

            Assert.True(WaitUntil(() => paragraph.Inlines.Single() is InlineUIContainer));
            var container = Assert.IsType<InlineUIContainer>(Assert.Single(paragraph.Inlines));
            var icon = Assert.IsAssignableFrom<FrameworkElement>(container.Child);
            Assert.Equal(16, icon.Width);
            Assert.Equal(16, icon.Height);
        });
    }

    [Fact]
    public void Render_RemoteMarkdownImage_LoadsBlockImageAsynchronously()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var layoutRefreshCount = 0;
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, resolver, requestLayoutRefresh: () => layoutRefreshCount++);
            var parser = new MarkdownParser();

            var document = renderer.Render(parser.Parse(
                "![](https://panuonui-silver-1252047526.cos.ap-chengdu.myqcloud.com/case_morin_4.png)"));

            var block = Assert.IsType<BlockUIContainer>(Assert.Single(document.Blocks.Cast<WpfBlock>()));
            var host = Assert.IsType<ContentControl>(block.Child);

            Assert.True(WaitUntil(() => resolver.ResolveStarted.Task.IsCompleted));
            resolver.Complete(Png1x1);

            Assert.True(
                WaitUntil(() => host.Content is Image image && image.Source is not null),
                $"Final content: {host.Content?.GetType().FullName}");
            var image = Assert.IsAssignableFrom<Image>(host.Content);
            Assert.NotNull(image.Source);
            Assert.True(WaitUntil(() => layoutRefreshCount > 0));
        });
    }

    [Fact]
    public void Render_RemoteMarkdownImage_KeepsStableHostWhenImageCompletes()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, resolver);
            var parser = new MarkdownParser();

            var document = renderer.Render(parser.Parse("![](https://example.com/stable.png)"));
            var block = Assert.IsType<BlockUIContainer>(Assert.Single(document.Blocks.Cast<WpfBlock>()));
            var host = Assert.IsType<ContentControl>(block.Child);

            resolver.Complete(Png1x1);

            Assert.True(WaitUntil(() => host.Content is Image image && image.Source is not null));
            Assert.Same(host, block.Child);
        });
    }

    [Fact]
    public void Render_SameRemoteMarkdownImage_UsesCachedImageData()
    {
        RunOnSta(() =>
        {
            var resolver = new CountingImageResolver(new ImageData { Data = Png1x1, Format = "png" });
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, resolver);
            var parser = new MarkdownParser();

            var markdown = "![](https://example.com/cached.png)";
            var firstDocument = renderer.Render(parser.Parse(markdown));
            var firstBlock = Assert.IsType<BlockUIContainer>(Assert.Single(firstDocument.Blocks.Cast<WpfBlock>()));
            var firstHost = Assert.IsType<ContentControl>(firstBlock.Child);
            Assert.True(WaitUntil(() => firstHost.Content is Image));

            var secondDocument = renderer.Render(parser.Parse(markdown));
            var secondBlock = Assert.IsType<BlockUIContainer>(Assert.Single(secondDocument.Blocks.Cast<WpfBlock>()));
            var secondHost = Assert.IsType<ContentControl>(secondBlock.Child);
            Assert.True(WaitUntil(() => secondHost.Content is Image));

            Assert.Equal(1, resolver.ResolveCount);
        });
    }

    [Fact]
    public void RenderIncremental_UnchangedImageBlock_ReusesLoadedBlockWithoutResolvingAgain()
    {
        RunOnSta(() =>
        {
            var resolver = new CountingImageResolver(new ImageData { Data = Png1x1, Format = "png" });
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, resolver);
            var parser = new MarkdownParser();

            const string firstMarkdown = "before\n\n![](https://example.com/reused.png)\n\nafter";
            var document = renderer.RenderIncremental(null, parser.Parse(firstMarkdown), firstMarkdown);
            var originalImageBlock = Assert.Single(document.Blocks.Cast<WpfBlock>().OfType<BlockUIContainer>());
            var originalHost = Assert.IsType<ContentControl>(originalImageBlock.Child);
            Assert.True(WaitUntil(() => originalHost.Content is Image));

            const string editedMarkdown = "changed before\n\n![](https://example.com/reused.png)\n\nchanged after";
            var updatedDocument = renderer.RenderIncremental(document, parser.Parse(editedMarkdown), editedMarkdown);
            var reusedImageBlock = Assert.Single(updatedDocument.Blocks.Cast<WpfBlock>().OfType<BlockUIContainer>());

            Assert.Same(document, updatedDocument);
            Assert.Same(originalImageBlock, reusedImageBlock);
            Assert.Equal(1, resolver.ResolveCount);
        });
    }

    [Fact]
    public void Render_RemoteMarkdownImage_UsesLoadingPlaceholderBeforeCompletion()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, resolver);
            var parser = new MarkdownParser();

            var document = renderer.Render(parser.Parse("![](https://example.com/case.png)"));

            var block = Assert.IsType<BlockUIContainer>(Assert.Single(document.Blocks.Cast<WpfBlock>()));
            Assert.Equal(TextAlignment.Left, block.TextAlignment);
            var host = Assert.IsType<ContentControl>(block.Child);
            var placeholder = Assert.IsType<Border>(host.Content);
            Assert.Equal(HorizontalAlignment.Left, placeholder.HorizontalAlignment);
            Assert.True(placeholder.Opacity > 0);
        });
    }

    [Fact]
    public void Render_RemoteMarkdownImageFailure_UsesLeftAlignedBrokenIcon()
    {
        RunOnSta(() =>
        {
            var resolver = new DeferredImageResolver();
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, resolver);
            var parser = new MarkdownParser();

            var document = renderer.Render(parser.Parse("![](https://example.com/missing.png)"));
            var block = Assert.IsType<BlockUIContainer>(Assert.Single(document.Blocks.Cast<WpfBlock>()));

            Assert.True(WaitUntil(() => resolver.ResolveStarted.Task.IsCompleted));
            resolver.Complete(null);

            var host = Assert.IsType<ContentControl>(block.Child);
            Assert.True(WaitUntil(() => host.Content is Canvas));
            var icon = Assert.IsType<Canvas>(host.Content);
            Assert.Equal(HorizontalAlignment.Left, icon.HorizontalAlignment);
        });
    }

    [Fact]
    public void Render_AnchorWrappedHtmlImageLines_UsesStandaloneImageLinkSpacing()
    {
        RunOnSta(() =>
        {
            var renderer = new FlowDocumentRenderer(EditorTheme.Light, new ImmediateImageResolver(null));
            var parser = new MarkdownParser();

            var document = renderer.Render(parser.Parse(
                """
                <a href="https://996.icu"><img src="https://img.shields.io/badge/link-996.icu-red.svg"></a>
                <a href="https://996.icu"><img src="https://camo.githubusercontent.com/image"></a>
                """));

            var section = Assert.IsType<Section>(Assert.Single(document.Blocks.Cast<WpfBlock>()));
            var paragraphs = section.Blocks.Cast<WpfBlock>().OfType<Paragraph>().ToList();

            Assert.Equal(2, paragraphs.Count);
            foreach (var paragraph in paragraphs)
            {
                Assert.Equal(8, paragraph.Margin.Top);
                Assert.Equal(12.8, paragraph.Margin.Bottom, precision: 2);
                Assert.Equal(1, CountInlines<Hyperlink>(paragraph.Inlines.Cast<System.Windows.Documents.Inline>()));
            }

            Assert.True(WaitUntil(() =>
                paragraphs.Sum(paragraph => CountInlines<InlineUIContainer>(paragraph.Inlines.Cast<System.Windows.Documents.Inline>())) == 2));
        });
    }

    private sealed class DeferredImageResolver : IImageResolver
    {
        private readonly TaskCompletionSource<ImageData?> _imageData = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> ResolveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ImageData?> ResolveImageAsync(string url, CancellationToken ct)
        {
            ResolveStarted.TrySetResult(url);
            return _imageData.Task;
        }

        public void Complete(byte[]? data, string format = "png") =>
            _imageData.TrySetResult(data is null ? null : new ImageData { Data = data, Format = format });
    }

    private sealed class ImmediateImageResolver(ImageData? imageData) : IImageResolver
    {
        public Task<ImageData?> ResolveImageAsync(string url, CancellationToken ct) => Task.FromResult(imageData);
    }

    private sealed class CountingImageResolver(ImageData? imageData) : IImageResolver
    {
        public int ResolveCount { get; private set; }

        public Task<ImageData?> ResolveImageAsync(string url, CancellationToken ct)
        {
            ResolveCount++;
            return Task.FromResult(imageData);
        }
    }

    private static int CountInlines<TInline>(IEnumerable<System.Windows.Documents.Inline> inlines)
        where TInline : System.Windows.Documents.Inline
    {
        var count = 0;
        foreach (var inline in inlines)
        {
            if (inline is TInline)
                count++;

            if (inline is Span span)
                count += CountInlines<TInline>(span.Inlines.Cast<System.Windows.Documents.Inline>());
        }

        return count;
    }

    private static IEnumerable<System.Windows.Documents.Inline> FlattenInlines(IEnumerable<System.Windows.Documents.Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
            {
                foreach (var child in FlattenInlines(span.Inlines.Cast<System.Windows.Documents.Inline>()))
                    yield return child;
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }

        return condition();
    }
}
