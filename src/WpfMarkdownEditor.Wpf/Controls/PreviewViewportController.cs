using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Controls;

internal readonly record struct PreviewAnchor(int SourceLine);

/// <summary>
/// Links the source pane and the preview pane: viewport anchoring, source-to-preview
/// scroll sync, active-block highlight and click-to-source.
/// </summary>
internal sealed class PreviewViewportController : IDisposable
{
    private readonly FlowDocumentScrollViewer _previewViewer;
    private readonly TextBox _source;
    private readonly Func<EditorTheme> _themeProvider;
    private readonly Action<int> _goToLine;

    private ScrollViewer? _previewScrollViewer;
    private ScrollViewer? _sourceScrollViewer;
    private WpfBlock? _highlightedBlock;
    private bool _syncQueued;
    private int _pendingSyncLine;
    private bool _isSyncing;

    public PreviewViewportController(
        FlowDocumentScrollViewer previewViewer,
        TextBox source,
        Func<EditorTheme> themeProvider,
        Action<int> goToLine)
    {
        _previewViewer = previewViewer;
        _source = source;
        _themeProvider = themeProvider;
        _goToLine = goToLine;

        _source.SelectionChanged += OnSourceSelectionChanged;
        _source.Loaded += OnSourceLoaded;
        _previewViewer.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;

        if (_source.IsLoaded)
            AttachSourceScrollViewer();
    }

    public bool EnableScrollSync { get; set; } = true;

    public bool HighlightActiveBlock { get; set; }

    /// <summary>
    /// Captures the source line that should stay in view after the next render.
    /// </summary>
    public PreviewAnchor? CaptureAnchor()
    {
        if (_source.ActualWidth <= 0)
            return null;

        var line = GetCaretLine();
        if (line < 1)
        {
            var firstVisible = _source.GetFirstVisibleLineIndex();
            line = firstVisible < 0 ? 0 : firstVisible + 1;
        }

        return line <= 0 ? null : new PreviewAnchor(line);
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

                var scrollViewer = GetPreviewScrollViewer();
                if (scrollViewer is null)
                    return;

                var block = FindBlockForLine(document, anchor.SourceLine);
                if (block is null)
                    return;

                ScrollBlockToTop(scrollViewer, document, block);
            }));
    }

    public void OnBeforeRender()
    {
        ClearHighlight();
    }

    public void OnAfterRender()
    {
        _highlightedBlock = null;
        UpdateActiveBlock();
    }

    /// <summary>
    /// Re-applies the active-block highlight and current scroll alignment after an option
    /// change (for example toggling scroll sync).
    /// </summary>
    public void Refresh()
    {
        UpdateActiveBlock();
        if (EnableScrollSync)
            SchedulePreviewSync(GetCaretLine());
    }

    /// <summary>
    /// Re-applies the active-block highlight without forcing a scroll. Used when a
    /// progressive render finishes while the user may have scrolled away.
    /// </summary>
    public void RefreshHighlight() => UpdateActiveBlock();

    public void Dispose()
    {
        _source.SelectionChanged -= OnSourceSelectionChanged;
        _source.Loaded -= OnSourceLoaded;
        _previewViewer.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;

        if (_sourceScrollViewer is not null)
            _sourceScrollViewer.ScrollChanged -= OnSourceScrollChanged;

        ClearHighlight();
    }

    private void OnSourceLoaded(object sender, RoutedEventArgs e) => AttachSourceScrollViewer();

    private void AttachSourceScrollViewer()
    {
        if (_sourceScrollViewer is not null)
            return;

        _sourceScrollViewer = FindDescendant<ScrollViewer>(_source);
        if (_sourceScrollViewer is not null)
            _sourceScrollViewer.ScrollChanged += OnSourceScrollChanged;
    }

    private void OnSourceSelectionChanged(object? sender, RoutedEventArgs e)
    {
        UpdateActiveBlock();
        if (EnableScrollSync)
            SchedulePreviewSync(GetCaretLine());
    }

    private void OnSourceScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!EnableScrollSync || _isSyncing || Math.Abs(e.VerticalChange) < 0.5)
            return;

        var firstVisibleLine = _source.GetFirstVisibleLineIndex();
        if (firstVisibleLine >= 0)
            SchedulePreviewSync(firstVisibleLine + 1);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        // Table cells manage their own selection; clicking a table must not jump
        // the source caret to the table's first line.
        if (TableSelectionController.IsTableSource(e.OriginalSource as DependencyObject))
            return;

        if (IsHyperlinkSource(e.OriginalSource as DependencyObject))
            return;

        if (!_previewViewer.Selection.IsEmpty)
            return;

        var block = FindTopLevelBlock(_previewViewer.Selection.Start);
        if (block is null || !RenderedBlockMetadata.TryGet(block, out var info))
            return;

        _goToLine(info.SourceStartLine);
    }

    private void UpdateActiveBlock()
    {
        if (!HighlightActiveBlock)
        {
            ClearHighlight();
            return;
        }

        var document = _previewViewer.Document;
        var line = GetCaretLine();
        if (document is null || line < 1)
        {
            ClearHighlight();
            return;
        }

        var block = FindBlockForLine(document, line);
        if (block is null)
        {
            ClearHighlight();
            return;
        }

        if (ReferenceEquals(block, _highlightedBlock))
            return;

        ClearHighlight();

        // Only body paragraphs get the optional focus tint. Headings, code, tables,
        // quotes and lists keep their own visual treatment (Typora uses a heading
        // rule, not a highlight rectangle).
        if (block is not Paragraph
            || (RenderedBlockMetadata.TryGet(block, out var info)
                && info.Signature.BlockType == typeof(HeadingBlock)))
        {
            return;
        }

        // Blocks rendered with their own background keep it.
        if (block.Background is not null)
            return;

        _highlightedBlock = block;
        block.Background = GetHighlightBrush();
    }

    private void ClearHighlight()
    {
        if (_highlightedBlock is null)
            return;

        _highlightedBlock.ClearValue(TextElement.BackgroundProperty);
        _highlightedBlock = null;
    }

    private SolidColorBrush GetHighlightBrush()
    {
        var link = _themeProvider().LinkColor;
        return EditorBrushCache.Get(Color.FromArgb(0x18, link.R, link.G, link.B));
    }

    private void SchedulePreviewSync(int line)
    {
        if (line < 1 || !EnableScrollSync)
            return;

        _pendingSyncLine = line;
        if (_syncQueued)
            return;

        _syncQueued = true;
        _previewViewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _syncQueued = false;
                SyncPreviewToLine(_pendingSyncLine);
            }));
    }

    private void SyncPreviewToLine(int line)
    {
        if (!EnableScrollSync || _isSyncing)
            return;

        var document = _previewViewer.Document;
        var scrollViewer = GetPreviewScrollViewer();
        if (document is null || scrollViewer is null)
            return;

        var block = FindBlockForLine(document, line);
        if (block is null)
            return;

        _isSyncing = true;
        try
        {
            ScrollBlockToTop(scrollViewer, document, block);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private static void ScrollBlockToTop(ScrollViewer scrollViewer, FlowDocument document, WpfBlock block)
    {
        var rect = block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
            return;

        var targetOffset = Math.Max(0, scrollViewer.VerticalOffset + rect.Top - document.PagePadding.Top);
        if (Math.Abs(targetOffset - scrollViewer.VerticalOffset) < 1)
            return;

        scrollViewer.ScrollToVerticalOffset(targetOffset);
    }

    private int GetCaretLine()
    {
        var line = _source.GetLineIndexFromCharacterIndex(_source.CaretIndex);
        return line < 0 ? 0 : line + 1;
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

    private static WpfBlock? FindTopLevelBlock(TextPointer position)
    {
        var element = position.Paragraph as TextElement;
        while (element is not null)
        {
            if (element.Parent is FlowDocument)
                return element as WpfBlock;

            element = element.Parent as TextElement;
        }

        return null;
    }

    private static bool IsHyperlinkSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Hyperlink)
                return true;

            source = source switch
            {
                FrameworkContentElement contentElement => contentElement.Parent,
                Visual => VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source),
            };
        }

        return false;
    }

    private ScrollViewer? GetPreviewScrollViewer()
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
