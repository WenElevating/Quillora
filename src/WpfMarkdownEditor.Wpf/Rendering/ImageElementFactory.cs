using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using WpfMarkdownEditor.Core;

namespace WpfMarkdownEditor.Wpf.Rendering;

internal static class ImageElementFactory
{
    private static readonly Regex NumberRegex = new(@"[-+]?\d*\.?\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ContentControl CreateImageHost(FrameworkElement initialContent) =>
        new()
        {
            Content = initialContent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Focusable = false,
        };

    public static void SetImageHostContent(ContentControl host, FrameworkElement content)
    {
        host.Content = content;
        host.InvalidateMeasure();
        host.InvalidateArrange();
        host.UpdateLayout();
    }

    public static bool IsSvg(ImageData imageData) =>
        string.Equals(imageData.Format, "svg", StringComparison.OrdinalIgnoreCase) ||
        Encoding.UTF8.GetString(imageData.Data.AsSpan(0, Math.Min(imageData.Data.Length, 256))).Contains("<svg", StringComparison.OrdinalIgnoreCase);

    public static BitmapImage? CreateBitmap(ImageData imageData)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            using (var stream = new MemoryStream(imageData.Data))
            {
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    public static Image CreateBitmapImageControl(
        BitmapImage bitmap,
        string? alt,
        double maxHeight,
        bool alignLeft,
        double? displayWidth = null,
        double? displayHeight = null)
    {
        var displaySize = ResolveDisplaySize(
            new Size(bitmap.PixelWidth, bitmap.PixelHeight),
            displayWidth,
            displayHeight);
        var image = new ResponsiveImage(displaySize.Width, displaySize.Height, maxHeight)
        {
            Source = bitmap,
            MaxHeight = maxHeight,
            HorizontalAlignment = alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 6, 0),
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
        };

        if (alt is not null)
            image.ToolTip = alt;

        return image;
    }

    public static WebBrowser? CreateSvgBrowser(
        ImageData imageData,
        string? alt,
        double maxHeight,
        double? displayWidth = null,
        double? displayHeight = null)
    {
        try
        {
            var naturalSize = ReadSvgSize(imageData.Data) ?? new Size(300, Math.Min(150, maxHeight));
            var size = ResolveDisplaySize(naturalSize, displayWidth, displayHeight);
            if (size.Height > maxHeight && maxHeight > 0)
            {
                var scale = maxHeight / size.Height;
                size = new Size(size.Width * scale, maxHeight);
            }

            var browser = new WebBrowser
            {
                Width = Math.Max(1, size.Width),
                Height = Math.Max(1, size.Height),
                HorizontalAlignment = HorizontalAlignment.Left,
                Focusable = false,
            };

            if (alt is not null)
                browser.ToolTip = alt;

            var svg = NormalizeSvgForBrowser(imageData.Data);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            browser.NavigateToString(
                $$"""
                <!doctype html>
                <html>
                <head>
                  <meta http-equiv="X-UA-Compatible" content="IE=edge" />
                  <style>
                    html, body { margin: 0; padding: 0; overflow: hidden; background: transparent; }
                    img { display: block; width: 100%; height: 100%; object-fit: contain; }
                  </style>
                </head>
                <body><img alt="" src="data:image/svg+xml;base64,{{base64}}" /></body>
                </html>
                """);

            return browser;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    public static FrameworkElement CreateBrokenImageIcon(string? tooltip)
    {
        var canvas = new Canvas
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = tooltip,
            SnapsToDevicePixels = true,
        };

        canvas.Children.Add(new Rectangle
        {
            Width = 13,
            Height = 13,
            Stroke = EditorBrushCache.Get(Color.FromRgb(0x8a, 0x8f, 0x98)),
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
        });

        canvas.Children.Add(new Line
        {
            X1 = 2,
            Y1 = 11,
            X2 = 6,
            Y2 = 7,
            Stroke = EditorBrushCache.Get(Color.FromRgb(0x8a, 0x8f, 0x98)),
            StrokeThickness = 1,
        });

        canvas.Children.Add(new Line
        {
            X1 = 6,
            Y1 = 7,
            X2 = 12,
            Y2 = 12,
            Stroke = EditorBrushCache.Get(Color.FromRgb(0x8a, 0x8f, 0x98)),
            StrokeThickness = 1,
        });

        var dot = new Ellipse
        {
            Width = 2,
            Height = 2,
            Fill = EditorBrushCache.Get(Color.FromRgb(0x8a, 0x8f, 0x98)),
        };
        canvas.Children.Add(dot);
        Canvas.SetLeft(dot, 9);
        Canvas.SetTop(dot, 3);

        return canvas;
    }

    public static FrameworkElement CreateLoadingImagePlaceholder(string? tooltip)
    {
        var borderBrush = EditorBrushCache.Get(Color.FromRgb(0xc9, 0xd1, 0xd9));
        borderBrush.Freeze();

        return new Border
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = tooltip,
            Background = Brushes.Transparent,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
        };
    }

    private static Size? ReadSvgSize(byte[] data)
    {
        try
        {
            var xml = Encoding.UTF8.GetString(data);
            var root = XDocument.Parse(xml).Root;
            if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                return null;

            var width = ParseSvgLength(root.Attribute("width")?.Value);
            var height = ParseSvgLength(root.Attribute("height")?.Value);
            if (width is > 0 && height is > 0)
                return new Size(width.Value, height.Value);

            var viewBox = root.Attribute("viewBox")?.Value;
            if (string.IsNullOrWhiteSpace(viewBox))
                return null;

            var values = NumberRegex.Matches(viewBox)
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
                .ToArray();
            return values.Length == 4 && values[2] > 0 && values[3] > 0
                ? new Size(values[2], values[3])
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    internal static string NormalizeSvgForBrowser(byte[] data)
    {
        var xml = Encoding.UTF8.GetString(data);
        try
        {
            var document = XDocument.Parse(xml);
            ReplaceForeignObjectText(document);
            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return xml;
        }
    }

    private static void ReplaceForeignObjectText(XDocument document)
    {
        XNamespace svgNamespace = "http://www.w3.org/2000/svg";
        var foreignObjects = document
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "foreignObject", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var foreignObject in foreignObjects)
        {
            var text = string.Join(
                " ",
                foreignObject.DescendantNodes()
                    .OfType<XText>()
                    .Select(static node => node.Value.Trim())
                    .Where(static value => value.Length > 0));
            if (text.Length == 0)
                continue;

            var style = ParseStyle(foreignObject.Attribute("style")?.Value);
            var x = ParseSvgLength(foreignObject.Attribute("x")?.Value) ?? 0;
            var y = ParseSvgLength(foreignObject.Attribute("y")?.Value) ?? 0;
            var width = ParseSvgLength(foreignObject.Attribute("width")?.Value);
            var fontSize = ParseSvgLength(GetStyleValue(style, "font-size")) ?? 12;
            var fontWeight = GetStyleValue(style, "font-weight");
            var fill = NormalizeCssColor(GetStyleValue(style, "color")) ?? "black";
            var textAnchor = GetTextAnchor(GetStyleValue(style, "text-align"));
            var baselineY = y + fontSize * 0.85;
            var textX = textAnchor == "middle" && width is > 0
                ? x + width.Value / 2
                : x;

            var replacement = new XElement(svgNamespace + "text",
                new XAttribute("x", FormatSvgNumber(textX)),
                new XAttribute("y", FormatSvgNumber(baselineY)),
                new XAttribute("fill", fill),
                new XAttribute("font-family", GetStyleValue(style, "font-family") ?? "Arial"),
                new XAttribute("font-size", FormatSvgNumber(fontSize)),
                new XAttribute("text-anchor", textAnchor),
                new XText(text));

            if (!string.IsNullOrWhiteSpace(fontWeight))
                replacement.SetAttributeValue("font-weight", fontWeight);

            foreignObject.ReplaceWith(replacement);
        }
    }

    private static Dictionary<string, string> ParseStyle(string? style)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
            return values;

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0)
                continue;

            values[declaration[..separator].Trim()] = declaration[(separator + 1)..].Trim();
        }

        return values;
    }

    private static string? GetStyleValue(IReadOnlyDictionary<string, string> style, string name) =>
        style.TryGetValue(name, out var value) ? value : null;

    private static string GetTextAnchor(string? textAlign) =>
        textAlign?.Trim().ToLowerInvariant() switch
        {
            "center" => "middle",
            "right" => "end",
            _ => "start"
        };

    private static string? NormalizeCssColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        var text = color.Trim();
        var match = Regex.Match(
            text,
            @"^rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return text;

        static int Clamp(int value) => Math.Max(0, Math.Min(255, value));
        var r = Clamp(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        var g = Clamp(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        var b = Clamp(int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        return FormattableString.Invariant($"#{r:X2}{g:X2}{b:X2}");
    }

    private static string FormatSvgNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static double? ParseSvgLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = NumberRegex.Match(value);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static Size ResolveDisplaySize(Size naturalSize, double? displayWidth, double? displayHeight)
    {
        var naturalWidth = IsFinitePositive(naturalSize.Width) ? naturalSize.Width : 300;
        var naturalHeight = IsFinitePositive(naturalSize.Height) ? naturalSize.Height : 150;
        var width = IsFinitePositive(displayWidth) ? displayWidth!.Value : double.NaN;
        var height = IsFinitePositive(displayHeight) ? displayHeight!.Value : double.NaN;

        if (IsFinitePositive(width) && IsFinitePositive(height))
            return new Size(width, height);

        if (IsFinitePositive(width))
            return new Size(width, Math.Max(1, naturalHeight * width / naturalWidth));

        if (IsFinitePositive(height))
            return new Size(Math.Max(1, naturalWidth * height / naturalHeight), height);

        return new Size(naturalWidth, naturalHeight);
    }

    private static bool IsFinitePositive(double? value) =>
        value is { } number && IsFinitePositiveNumber(number);

    private static bool IsFinitePositiveNumber(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private sealed class ResponsiveImage(double naturalWidth, double naturalHeight, double maxHeight) : Image
    {
        protected override Size MeasureOverride(Size constraint)
        {
            var desired = ConstrainSize(naturalWidth, naturalHeight, ResolveAvailableWidth(constraint.Width), maxHeight);
            base.MeasureOverride(desired);
            return desired;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var desired = ConstrainSize(naturalWidth, naturalHeight, ResolveAvailableWidth(arrangeSize.Width), maxHeight);
            return base.ArrangeOverride(desired);
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            Dispatcher.BeginInvoke(new Action(InvalidateMeasure), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private double ResolveAvailableWidth(double constraintWidth)
        {
            if (IsFinitePositive(constraintWidth))
                return constraintWidth;

            var viewer = FindAncestor<FlowDocumentScrollViewer>();
            if (viewer?.Document is null || !IsFinitePositive(viewer.ActualWidth))
                return double.PositiveInfinity;

            var padding = viewer.Document.PagePadding;
            return Math.Max(1, viewer.ActualWidth - padding.Left - padding.Right);
        }

        private static Size ConstrainSize(double width, double height, double maxWidth, double maxHeight)
        {
            if (width <= 0 || height <= 0)
                return new Size(double.NaN, double.NaN);

            var scale = 1d;
            if (IsFinitePositive(maxWidth) && width * scale > maxWidth)
                scale = maxWidth / width;

            if (IsFinitePositive(maxHeight) && height * scale > maxHeight)
                scale = Math.Min(scale, maxHeight / height);

            return new Size(Math.Max(1, width * scale), Math.Max(1, height * scale));
        }

        private T? FindAncestor<T>() where T : DependencyObject
        {
            DependencyObject? current = this;
            while (current is not null)
            {
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
                if (current is T match)
                    return match;
            }

            return null;
        }

        private static bool IsFinitePositive(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
