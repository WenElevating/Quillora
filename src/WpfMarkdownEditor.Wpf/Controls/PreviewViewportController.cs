using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Controls;

internal readonly record struct PreviewAnchor(int SourceLine);

/// <summary>
/// Keeps the preview viewport anchored to the source line it was showing before a render.
/// Phase 2 extends this type with scroll sync, active-block highlight and click-to-source.
/// </summary>
internal sealed class PreviewViewportController
{
    private readonly FlowDocumentScrollViewer _previewViewer;
    private readonly TextBox _source;
    private ScrollViewer? _previewScrollViewer;

    public PreviewViewportController(FlowDocumentScrollViewer previewViewer, TextBox source)
    {
        _previewViewer = previewViewer;
        _source = source;
    }

    /// <summary>
    /// Captures the source line that should stay in view after the next render.
    /// The caret line wins because it is the editing focus; when no caret line is
    /// available (source pane not laid out yet) the first visible line is used.
    /// </summary>
    public PreviewAnchor? CaptureAnchor()
    {
        // Reading mode hides the source pane; there is no source caret to anchor to.
        if (_source.ActualWidth <= 0)
            return null;

        var line = _source.GetLineIndexFromCharacterIndex(_source.CaretIndex);
        if (line < 0)
            line = _source.GetFirstVisibleLineIndex();

        return line < 0 ? null : new PreviewAnchor(line + 1);
    }

    /// <summary>
    /// Scrolls the preview so the block containing <paramref name="anchor"/> is at the top.
    /// </summary>
    public void RestoreAnchor(PreviewAnchor anchor)
    {
        _previewViewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                var document = _previewViewer.Document;
                if (document is null)
                    return;

                var scrollViewer = GetScrollViewer();
                if (scrollViewer is null)
                    return;

                var block = FindBlockForLine(document, anchor.SourceLine);
                if (block is null)
                    return;

                var rect = block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (rect.IsEmpty)
                    return;

                var targetOffset = scrollViewer.VerticalOffset + rect.Top - document.PagePadding.Top;
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, targetOffset));
            }));
    }

    private static WpfBlock? FindBlockForLine(FlowDocument document, int line)
    {
        WpfBlock? firstFollowingBlock = null;
        WpfBlock? lastMappedBlock = null;
        var hasMetadata = false;

        foreach (var block in document.Blocks)
        {
            if (!RenderedBlockMetadata.TryGet(block, out var info))
                continue;

            hasMetadata = true;
            lastMappedBlock = block;

            if (info.SourceStartLine <= line && line <= info.SourceEndLine)
                return block;

            if (info.SourceStartLine > line && firstFollowingBlock is null)
                firstFollowingBlock = block;
        }

        return hasMetadata ? firstFollowingBlock ?? lastMappedBlock : null;
    }

    private ScrollViewer? GetScrollViewer()
    {
        _previewScrollViewer ??= FindDescendant<ScrollViewer>(_previewViewer);
        return _previewScrollViewer;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
                return match;

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < childCount; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }

        return null;
    }
}
