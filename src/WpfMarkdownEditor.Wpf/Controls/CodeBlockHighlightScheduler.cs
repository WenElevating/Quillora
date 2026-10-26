using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Highlights only the code blocks that intersect the preview viewport, and re-checks
/// as the user scrolls. Keeps large code-heavy documents from creating thousands of
/// offscreen WPF text runs during the initial render.
/// </summary>
internal sealed class CodeBlockHighlightScheduler : IDisposable
{
    private readonly FlowDocumentScrollViewer _viewer;
    private readonly Action _requestLayoutRefresh;
    private readonly List<CodeBlockTarget> _pending = [];
    private ScrollViewer? _scrollViewer;
    private bool _queued;
    private bool _disposed;

    public CodeBlockHighlightScheduler(FlowDocumentScrollViewer viewer, Action requestLayoutRefresh)
    {
        _viewer = viewer;
        _requestLayoutRefresh = requestLayoutRefresh;
        _viewer.Loaded += OnViewerLoaded;
        _viewer.SizeChanged += OnViewerSizeChanged;

        if (_viewer.IsLoaded)
            AttachScrollViewer();
    }

    public void OnBeforeRender()
    {
        _pending.Clear();
    }

    public void OnDocumentRendered()
    {
        _pending.Clear();
        CollectTargets(_viewer.Document);
        if (_pending.Count > 0)
            ScheduleHighlight();
    }

    /// <summary>
    /// Collects code blocks appended by a progressive render chunk without rescanning
    /// the whole document.
    /// </summary>
    public void OnBlocksAppended(IReadOnlyList<WpfBlock> blocks)
    {
        if (_disposed)
            return;

        foreach (var block in blocks)
            CollectBlock(block);

        if (_pending.Count > 0)
            ScheduleHighlight();
    }

    public void Dispose()
    {
        _disposed = true;
        _viewer.Loaded -= OnViewerLoaded;
        _viewer.SizeChanged -= OnViewerSizeChanged;
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;

        _pending.Clear();
    }

    private void OnViewerLoaded(object sender, RoutedEventArgs e) => AttachScrollViewer();

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_disposed && _pending.Count > 0)
            ScheduleHighlight();
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindDescendant<ScrollViewer>(_viewer);
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_disposed || _pending.Count == 0 || Math.Abs(e.VerticalChange) < 0.5)
            return;

        ScheduleHighlight();
    }

    private void ScheduleHighlight()
    {
        if (_queued || _disposed)
            return;

        _queued = true;
        _viewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                _queued = false;
                HighlightVisibleTargets();
            }));
    }

    private void HighlightVisibleTargets()
    {
        if (_disposed || _pending.Count == 0)
            return;

        var viewportHeight = _viewer.ActualHeight;
        if (viewportHeight <= 0)
            return;

        var remaining = new List<CodeBlockTarget>(_pending.Count);
        var changed = false;

        foreach (var target in _pending)
        {
            var rect = target.Paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty || rect.Bottom < 0 || rect.Top > viewportHeight)
            {
                remaining.Add(target);
                continue;
            }

            target.Highlight();
            changed = true;
        }

        _pending.Clear();
        _pending.AddRange(remaining);

        if (changed)
            _requestLayoutRefresh();
    }

    private void CollectTargets(FlowDocument? document)
    {
        if (document is null)
            return;

        foreach (var block in document.Blocks)
            CollectBlock(block);
    }

    private void CollectBlock(WpfBlock block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                var target = CodeBlockHighlightMetadata.Get(paragraph);
                if (target is { IsHighlighted: false })
                    _pending.Add(target);
                break;

            case Section section:
                foreach (var child in section.Blocks)
                    CollectBlock(child);
                break;

            case WpfList list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                        CollectBlock(child);
                }
                break;
        }
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
