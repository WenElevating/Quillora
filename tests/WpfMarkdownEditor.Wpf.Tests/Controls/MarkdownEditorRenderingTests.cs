using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Controls;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

public sealed class MarkdownEditorRenderingTests
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

    [Fact]
    public void MarkdownChange_RefreshesPreviewDocumentBindingWhenDocumentIsReused()
    {
        RunOnSta(() =>
        {
            using var editor = new MarkdownEditor
            {
                Markdown =
                    """
                    before

                    ![](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGMAAQAABQABDQotxAAAAABJRU5ErkJggg==)

                    after
                    """,
            };

            Assert.True(WaitUntil(() => editor.PreviewViewer.Document is not null));
            var initialDocument = editor.PreviewViewer.Document;
            Assert.NotNull(initialDocument);

            var documentChangeCount = 0;
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                FlowDocumentScrollViewer.DocumentProperty,
                typeof(FlowDocumentScrollViewer));
            descriptor.AddValueChanged(editor.PreviewViewer, OnDocumentChanged);
            try
            {
                editor.Markdown =
                    """
                    changed before

                    ![](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGMAAQAABQABDQotxAAAAABJRU5ErkJggg==)

                    changed after
                    """;

                Assert.True(WaitUntil(() => documentChangeCount >= 2));
                Assert.Same(initialDocument, editor.PreviewViewer.Document);
            }
            finally
            {
                descriptor.RemoveValueChanged(editor.PreviewViewer, OnDocumentChanged);
            }

            void OnDocumentChanged(object? sender, EventArgs e) => documentChangeCount++;
        });
    }

    [Fact]
    public void LoadFile_UsesDocumentDirectoryForRelativeImages()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "images");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(imageDirectory);
            File.WriteAllText(markdownPath, "![logo](images/logo.png)");
            File.WriteAllBytes(Path.Combine(imageDirectory, "logo.png"), Png1x1);

            try
            {
                using var editor = new MarkdownEditor();

                editor.LoadFile(markdownPath);

                Assert.True(WaitUntil(() => DocumentContainsImage(editor.PreviewViewer.Document)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void SaveClipboardImage_WithDocumentBaseDirectory_WritesRelativeImageBesideDocument()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var relativePath = ImagePasteService.SaveClipboardImage(CreateBitmapSource(width: 2, height: 2), root);

                Assert.NotNull(relativePath);
                Assert.StartsWith("images/clipboard_", relativePath);
                Assert.EndsWith(".png", relativePath);
                Assert.True(File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void PasteImageStyleMarkdown_UnsavedDocument_RendersImageFromApplicationImagesDirectory()
    {
        RunOnSta(() =>
        {
            var imagesDirectory = Path.Combine(AppContext.BaseDirectory, "images");
            Directory.CreateDirectory(imagesDirectory);
            var relativePath = ImagePasteService.SaveClipboardImage(
                CreateBitmapSource(width: 1545, height: 1356),
                AppContext.BaseDirectory);
            Assert.NotNull(relativePath);

            var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                using var editor = new MarkdownEditor
                {
                    Width = 1000,
                    Height = 800,
                    Markdown = $"![{Path.GetFileName(relativePath)}]({relativePath})",
                };

                editor.Measure(new Size(1000, 800));
                editor.Arrange(new Rect(0, 0, 1000, 800));
                editor.UpdateLayout();

                Assert.True(WaitUntil(() => FindImage(editor.PreviewViewer.Document) is { Source: not null }));
                var image = FindImage(editor.PreviewViewer.Document);
                Assert.NotNull(image);
                Assert.True(image.ActualWidth > 0 && image.ActualHeight > 0);
            }
            finally
            {
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
        });
    }

    [Fact]
    public void SaveClipboardImage_TransparentClipboardRgbData_WritesOpaquePng()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var relativePath = ImagePasteService.SaveClipboardImage(CreateTransparentRgbBitmapSource(), root);

                Assert.NotNull(relativePath);
                var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(fullPath), $"Pasted image file was not written: {fullPath}");
                Assert.Equal(4, CountPixelsWithAlpha(fullPath, 255));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void LoadFile_RendersRelativeImageAfterSingleLineHtmlHeading()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "images");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(imageDirectory);
            File.WriteAllText(
                markdownPath,
                """
                <h1 align="center">Title</h1>
                ![clipboard](images/clipboard.png)

                <p align="center">Next paragraph</p>
                """);
            File.WriteAllBytes(Path.Combine(imageDirectory, "clipboard.png"), Png1x1);

            try
            {
                using var editor = new MarkdownEditor();

                editor.LoadFile(markdownPath);

                Assert.True(WaitUntil(() => DocumentContainsImage(editor.PreviewViewer.Document)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void LoadFile_RendersLargeRelativeClipboardImageAfterSingleLineHtmlHeading()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "images");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(imageDirectory);
            File.WriteAllText(
                markdownPath,
                """
                <h1 align="center">Title</h1>
                ![clipboard](images/clipboard.png)

                <p align="center">Next paragraph</p>
                """);
            WritePng(Path.Combine(imageDirectory, "clipboard.png"), width: 1442, height: 962);

            try
            {
                using var editor = new MarkdownEditor
                {
                    Width = 1000,
                    Height = 800,
                };

                editor.LoadFile(markdownPath);
                editor.Measure(new Size(1000, 800));
                editor.Arrange(new Rect(0, 0, 1000, 800));
                editor.UpdateLayout();

                Assert.True(WaitUntil(() => FindImage(editor.PreviewViewer.Document) is { Source: not null }));
                var image = FindImage(editor.PreviewViewer.Document);
                Assert.NotNull(image);
                Assert.True(image.ActualHeight > 0 || image.Height > 0);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void LoadFile_RendersRelativeImageBetweenSingleLineHtmlBlocksWithoutBlankLines()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "images");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(imageDirectory);
            File.WriteAllText(
                markdownPath,
                """
                <h1 align="center">Title</h1>
                ![clipboard](images/clipboard.png)
                <p align="center">Next paragraph</p>
                """);
            WritePng(Path.Combine(imageDirectory, "clipboard.png"), width: 1442, height: 962);

            try
            {
                using var editor = new MarkdownEditor
                {
                    Width = 1000,
                    Height = 800,
                };

                editor.LoadFile(markdownPath);
                editor.Measure(new Size(1000, 800));
                editor.Arrange(new Rect(0, 0, 1000, 800));
                editor.UpdateLayout();

                Assert.True(WaitUntil(() => FindImage(editor.PreviewViewer.Document) is { Source: not null }));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void MarkdownChange_WithDocumentPath_RendersClipboardStyleRelativeImages()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "images");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(imageDirectory);
            WritePng(Path.Combine(imageDirectory, "clipboard_20260604_224533.png"), width: 1442, height: 962);
            WritePng(Path.Combine(imageDirectory, "clipboard_20260604_222406.png"), width: 1442, height: 962);

            try
            {
                using var editor = new MarkdownEditor
                {
                    Width = 1000,
                    Height = 800,
                    DocumentPath = markdownPath,
                };

                editor.Markdown =
                    """
                    ![clipboard_20260604_222406.png](images/clipboard_20260604_222406.png)

                    ![clipboard_20260604_224533.png](images/clipboard_20260604_224533.png)
                    """;
                editor.Measure(new Size(1000, 800));
                editor.Arrange(new Rect(0, 0, 1000, 800));
                editor.UpdateLayout();

                Assert.True(WaitUntil(() => CountImages(editor.PreviewViewer.Document) == 2));
                editor.Measure(new Size(1000, 800));
                editor.Arrange(new Rect(0, 0, 1000, 800));
                editor.UpdateLayout();

                var images = FindImages(editor.PreviewViewer.Document).ToArray();
                Assert.Equal(2, images.Length);
                Assert.All(images, image => Assert.True(
                    image.ActualWidth > 0 && image.ActualHeight > 0,
                    $"Image was loaded but not visible. Actual size: {image.ActualWidth}x{image.ActualHeight}."));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void MarkdownChange_AfterInitialMissingRelativeImage_ReRendersWhenDocumentPathIsSet()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imageDirectory = Path.Combine(root, "images");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(imageDirectory);
            WritePng(Path.Combine(imageDirectory, "clipboard.png"), width: 1442, height: 962);

            try
            {
                using var editor = new MarkdownEditor
                {
                    Width = 1000,
                    Height = 800,
                };

                editor.Markdown = "![clipboard.png](images/clipboard.png)";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document is not null));

                editor.DocumentPath = markdownPath;
                editor.Measure(new Size(1000, 800));
                editor.Arrange(new Rect(0, 0, 1000, 800));
                editor.UpdateLayout();

                Assert.True(WaitUntil(() => FindImage(editor.PreviewViewer.Document) is { Source: not null }));
                var image = FindImage(editor.PreviewViewer.Document);
                Assert.NotNull(image);
                Assert.True(
                    image.ActualWidth > 0 && image.ActualHeight > 0,
                    $"Image was loaded but not visible. Actual size: {image.ActualWidth}x{image.ActualHeight}.");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public void PreviewImage_WiderThanPreviewPane_ShrinksToAvailableWidth()
    {
        RunOnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WpfMarkdownEditor.Tests", Guid.NewGuid().ToString("N"));
            var imagePath = Path.Combine(root, "wide.png");
            var markdownPath = Path.Combine(root, "readme.md");
            Directory.CreateDirectory(root);
            WritePng(imagePath, width: 1200, height: 300);
            File.WriteAllText(markdownPath, "![wide](wide.png)");

            try
            {
                using var editor = new MarkdownEditor
                {
                    Width = 700,
                    Height = 500,
                };

                editor.LoadFile(markdownPath);
                editor.Measure(new Size(700, 500));
                editor.Arrange(new Rect(0, 0, 700, 500));
                editor.UpdateLayout();

                Assert.True(WaitUntil(() => FindImage(editor.PreviewViewer.Document) is { Source: not null }));
                editor.Measure(new Size(700, 500));
                editor.Arrange(new Rect(0, 0, 700, 500));
                editor.UpdateLayout();

                var image = FindImage(editor.PreviewViewer.Document);
                Assert.NotNull(image);
                var availableWidth = editor.PreviewViewer.ActualWidth - editor.PreviewViewer.Document.PagePadding.Left - editor.PreviewViewer.Document.PagePadding.Right;

                Assert.True(
                    image.ActualWidth <= availableWidth,
                    $"Image width {image.ActualWidth} exceeds preview width {availableWidth}.");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
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

    private static bool DocumentContainsImage(FlowDocument? document)
    {
        if (document is null)
            return false;

        return document.Blocks.Cast<Block>().Any(BlockContainsImage);
    }

    private static bool BlockContainsImage(Block block) =>
        block switch
        {
            BlockUIContainer container => container.Child is Image image && image.Source is not null ||
                                          container.Child is ContentControl host && host.Content is Image hostImage && hostImage.Source is not null,
            Section section => section.Blocks.Cast<Block>().Any(BlockContainsImage),
            Paragraph paragraph => paragraph.Inlines.Cast<Inline>().Any(InlineContainsImage),
            Table table => table.RowGroups
                .SelectMany(group => group.Rows)
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks.Cast<Block>())
                .Any(BlockContainsImage),
            _ => false
        };

    private static bool InlineContainsImage(Inline inline) =>
        inline switch
        {
            InlineUIContainer container => container.Child is Image image && image.Source is not null,
            Span span => span.Inlines.Cast<Inline>().Any(InlineContainsImage),
            _ => false
        };

    private static Image? FindImage(FlowDocument? document)
    {
        if (document is null)
            return null;

        return document.Blocks.Cast<Block>().Select(FindImage).FirstOrDefault(static image => image is not null);
    }

    private static IEnumerable<Image> FindImages(FlowDocument? document)
    {
        if (document is null)
            return [];

        return document.Blocks.Cast<Block>().SelectMany(FindImages);
    }

    private static int CountImages(FlowDocument? document)
    {
        if (document is null)
            return 0;

        return document.Blocks.Cast<Block>().Sum(CountImages);
    }

    private static Image? FindImage(Block block) =>
        block switch
        {
            BlockUIContainer { Child: Image image } => image,
            BlockUIContainer { Child: ContentControl { Content: Image image } } => image,
            Section section => section.Blocks.Cast<Block>().Select(FindImage).FirstOrDefault(static image => image is not null),
            Paragraph paragraph => paragraph.Inlines.Cast<Inline>().Select(FindImage).FirstOrDefault(static image => image is not null),
            Table table => table.RowGroups
                .SelectMany(group => group.Rows)
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks.Cast<Block>())
                .Select(FindImage)
                .FirstOrDefault(static image => image is not null),
            _ => null
        };

    private static IEnumerable<Image> FindImages(Block block) =>
        block switch
        {
            BlockUIContainer { Child: Image image } => [image],
            BlockUIContainer { Child: ContentControl { Content: Image image } } => [image],
            Section section => section.Blocks.Cast<Block>().SelectMany(FindImages),
            Paragraph paragraph => paragraph.Inlines.Cast<Inline>().SelectMany(FindImages),
            Table table => table.RowGroups
                .SelectMany(group => group.Rows)
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks.Cast<Block>())
                .SelectMany(FindImages),
            _ => []
        };

    private static int CountImages(Block block) =>
        block switch
        {
            BlockUIContainer { Child: Image image } when image.Source is not null => 1,
            BlockUIContainer { Child: ContentControl { Content: Image image } } when image.Source is not null => 1,
            Section section => section.Blocks.Cast<Block>().Sum(CountImages),
            Paragraph paragraph => paragraph.Inlines.Cast<Inline>().Sum(CountImages),
            Table table => table.RowGroups
                .SelectMany(group => group.Rows)
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks.Cast<Block>())
                .Sum(CountImages),
            _ => 0
        };

    private static Image? FindImage(Inline inline) =>
        inline switch
        {
            InlineUIContainer { Child: Image image } => image,
            Span span => span.Inlines.Cast<Inline>().Select(FindImage).FirstOrDefault(static image => image is not null),
            _ => null
        };

    private static IEnumerable<Image> FindImages(Inline inline) =>
        inline switch
        {
            InlineUIContainer { Child: Image image } => [image],
            Span span => span.Inlines.Cast<Inline>().SelectMany(FindImages),
            _ => []
        };

    private static int CountImages(Inline inline) =>
        inline switch
        {
            InlineUIContainer { Child: Image image } when image.Source is not null => 1,
            Span span => span.Inlines.Cast<Inline>().Sum(CountImages),
            _ => 0
        };

    private static void WritePng(string path, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        }

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource CreateBitmapSource(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x00;
            pixels[i + 1] = 0x80;
            pixels[i + 2] = 0xFF;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateTransparentRgbBitmapSource()
    {
        var pixels = new byte[2 * 2 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x20;
            pixels[i + 1] = 0x80;
            pixels[i + 2] = 0xFF;
            pixels[i + 3] = 0x00;
        }

        var bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            2 * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static int CountPixelsWithAlpha(string path, byte expectedAlpha)
    {
        var decoder = BitmapDecoder.Create(
            new Uri(path),
            BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] == expectedAlpha)
                count++;
        }

        return count;
    }
}
