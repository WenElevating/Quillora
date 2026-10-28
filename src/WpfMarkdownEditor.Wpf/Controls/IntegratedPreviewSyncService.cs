using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfMarkdownEditor.Wpf.Theming;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Drives the integrated (Typora-style) edit+preview pane. The source <see cref="TextBox"/>
/// is rendered transparently on top of a <see cref="FlowDocumentScrollViewer"/> so the user
/// sees the formatted markdown while typing, with the raw source only where the caret is.
/// </summary>
internal sealed class IntegratedPreviewSyncService : IDisposable
{
    private readonly FlowDocumentScrollViewer _previewViewer;
    private readonly TextBox _source;
    private readonly Func<string> _markdownProvider;
    private readonly Func<EditorTheme> _themeProvider;
    private readonly Func<string> _baseDirectoryProvider;
    private readonly PreviewViewportController _viewportController;
    private readonly PreviewRenderController _renderController;
    private readonly Action<int>? _syncCaretToPreview;
    private readonly Action? _syncPreviewToCaret;
    private readonly Action<PreviewRenderedEventArgs>? _onRendered;
    private ScrollViewer? _previewScrollViewer;
    private bool _isSyncing;
    private bool _disposed;

    public IntegratedPreviewSyncService(
        FlowDocumentScrollViewer previewViewer,
        TextBox source,
        Func<string> markdownProvider,
        Func<EditorTheme> themeProvider,
        Func<string> baseDirectoryProvider,
        Action<int>? syncCaretToPreview,
        Action? syncPreviewToCaret,
        Action<PreviewRenderedEventArgs>? onRendered)
    {
        _previewViewer = previewViewer;
        _source = source;
        _markdownProvider = markdownProvider;
        _themeProvider = themeProvider;
        _baseDirectoryProvider = baseDirectoryProvider;
        _syncCaretToPreview = syncCaretToPreview;
        _syncPreviewToCaret = syncPreviewToCaret;
        _onRendered = onRendered;

        _viewportController = new PreviewViewportController(
            previewViewer,
            source,
            themeProvider,
            line => { /* integrated mode has no separate GoToLine target */ });

        _renderController = new PreviewRenderController(
            previewViewer,
            markdownProvider,
            themeProvider,
            baseDirectoryProvider,
            _viewportController,
            onRendered);

        // The transparent source stays pinned to its caret position while the preview
// scrolls, so no preview->caret sync is needed here.
    }

    public void OnTextChanged()
    {
        if (_disposed) return;
        _renderController.OnMarkdownChanged();
    }

    public void SyncCaretToPreview()
    {
        if (_disposed) return;
        _syncCaretToPreview?.Invoke(SourceLineMapper.GetSourceLine(_source, _source.CaretIndex));
    }

    public void SyncPreviewToCaret()
    {
        if (_disposed) return;
        _syncPreviewToCaret?.Invoke();
    }

    public void OnLostFocus()
    {
        // Nothing to do in integrated mode; the preview stays in place.
    }

    public void OnGotFocus()
    {
        SyncCaretToPreview();
    }

    public void OnMouseDown()
    {
        SyncCaretToPreview();
    }

    public void OnMouseWheel(int delta)
    {
        if (_disposed) return;
        // Forward the wheel directly to the preview so the rendered document scrolls
        // under the transparent source instead of the source's own scrollbar.
        var scrollViewer = _previewScrollViewer ??= FindDescendant<ScrollViewer>(_previewViewer);
        if (scrollViewer is null) return;

        _isSyncing = true;
        try
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + delta / 120.0);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    public void OnThemeChanged() => _renderController.OnThemeChanged();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _renderController.Dispose();
        _viewportController.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnPreviewScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_disposed || _isSyncing) return;
        _syncPreviewToCaret?.Invoke();
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

        return default;
    }
}

