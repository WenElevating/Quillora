using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Handles pasting clipboard images and image files as Markdown image blocks.
/// </summary>
internal sealed class ImagePasteService(Func<string> getDocumentBaseDirectory)
{
    public bool CanPasteImage()
    {
        try
        {
            if (Clipboard.ContainsImage())
                return true;

            if (!Clipboard.ContainsFileDropList())
                return false;

            var files = Clipboard.GetFileDropList();
            return files.Cast<string?>().Any(IsSupportedImagePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Tries to insert an image from the clipboard. Returns false when the clipboard
    /// does not contain a supported image so the caller can fall back to text paste.
    /// </summary>
    public bool TryPasteImage(
        string text,
        int selectionStart,
        int selectionLength,
        Action<TextEditOperation> applyEdit)
    {
        // Priority 1: Clipboard image (screenshot, copied image)
        if (Clipboard.ContainsImage())
        {
            var imageSource = Clipboard.GetImage();
            if (imageSource != null)
            {
                var imagePath = SaveClipboardImage(imageSource, getDocumentBaseDirectory());
                if (imagePath != null)
                {
                    InsertImageMarkdown(text, selectionStart, selectionLength, CreateImageMarkdown(imagePath), applyEdit);
                    return true;
                }
            }
        }

        // Priority 2: File drop list (copied image file from Explorer)
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            foreach (string? file in files)
            {
                if (IsSupportedImagePath(file))
                {
                    InsertImageMarkdown(text, selectionStart, selectionLength, CreateImageMarkdown(file!), applyEdit);
                    return true;
                }
            }
        }

        return false;
    }

    private static void InsertImageMarkdown(
        string text,
        int selectionStart,
        int selectionLength,
        string markdown,
        Action<TextEditOperation> applyEdit)
    {
        var operation = EditorTextOperations.InsertImageBlock(text, selectionStart, selectionLength, markdown);
        applyEdit(operation);
    }

    private static bool IsSupportedImagePath(string? file)
    {
        if (file == null) return false;
        var ext = Path.GetExtension(file).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg";
    }

    private static string CreateImageMarkdown(string imagePath)
    {
        var fileName = Path.GetFileName(imagePath);
        var altText = EscapeImageAltText(fileName);
        var destination = FormatMarkdownImageDestination(imagePath);
        return $"![{altText}]({destination})";
    }

    private static string EscapeImageAltText(string value) =>
        value.Replace("\\", "\\\\")
            .Replace("[", "\\[")
            .Replace("]", "\\]");

    private static string FormatMarkdownImageDestination(string imagePath)
    {
        var normalized = imagePath.Replace('\\', '/')
            .Replace("<", "%3C")
            .Replace(">", "%3E");

        return normalized.Any(c => char.IsWhiteSpace(c) || c is '(' or ')')
            ? $"<{normalized}>"
            : normalized;
    }

    internal static string? SaveClipboardImage(BitmapSource image, string baseDirectory)
    {
        try
        {
            var imagesDir = Path.Combine(baseDirectory, "images");
            Directory.CreateDirectory(imagesDir);

            var fileName = $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(imagesDir, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(EnsureVisibleClipboardImage(image)));

            using var stream = File.Create(filePath);
            encoder.Save(stream);

            return $"images/{fileName}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private static BitmapSource EnsureVisibleClipboardImage(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var alphaNonZero = false;
        var rgbNonZero = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] != 0)
                alphaNonZero = true;

            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                rgbNonZero = true;

            if (alphaNonZero)
                return image;
        }

        if (!rgbNonZero)
            return image;

        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;

        var visible = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        visible.Freeze();
        return visible;
    }
}
