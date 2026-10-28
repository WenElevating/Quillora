using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Typora-style block editing for the integrated pane. The rendered document fills the
/// pane; clicking a block swaps that single block into a source editor positioned over its
/// rect. Committing writes the block's markdown back into the document so the block renders
/// again. Only one block is editable at a time, matching Typora's md-expand behaviour.
/// </summary>
internal sealed class IntegratedBlockEditor : IDisposable
{
    private readonly FlowDocumentScrollViewer _viewer;
    private readonly Canvas _layer;
    private readonly Func<EditorTheme> _themeProvider;
    private readonly Func<string> _markdownProvider;
    private readonly Action<string> _applyMarkdown;
    private readonly TextBox _editor;
    private readonly Border _host;

    private ScrollViewer? _scrollViewer;
    private int _startLine;
    private int _endLine;
    private bool _isEditing;
    private bool _isCommitting;
    private bool _suppressFocusClose;
    private bool _disposed;

    public IntegratedBlockEditor(
        FlowDocumentScrollViewer viewer,
        Canvas layer,
        Func<EditorTheme> themeProvider,
        Func<string> markdownProvider,
        Action<string> applyMarkdown)
    {
        _viewer = viewer;
        _layer = layer;
        _themeProvider = themeProvider;
        _markdownProvider = markdownProvider;
        _applyMarkdown = applyMarkdown;

        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
        };

        _host = new Border
        {
            Child = _editor,
            Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true,
        };

        _layer.Children.Add(_host);

        _editor.LostKeyboardFocus += OnEditorLostKeyboardFocus;
        _editor.PreviewKeyDown += OnEditorPreviewKeyDown;

        _viewer.MouseLeftButtonUp += OnViewerMouseLeftButtonUp;
        _viewer.SizeChanged += OnViewerSizeChanged;
        _viewer.Loaded += OnViewerLoaded;

        if (_viewer.IsLoaded)
            AttachScrollViewer();

        ApplyTheme();
    }

    /// <summary>
    /// True while a block is open for editing.
    /// </summary>
    public bool IsEditing => _isEditing;

    /// <summary>
    /// Refreshes the inline editor chrome after a theme change.
    /// </summary>
    public void ApplyTheme()
    {
        var theme = _themeProvider();

        var background = new SolidColorBrush(theme.BackgroundColor);
        background.Freeze();
        var foreground = new SolidColorBrush(theme.EditorForegroundColor);
        foreground.Freeze();
        var accent = new SolidColorBrush(theme.LinkColor);
        accent.Freeze();
        var caret = new SolidColorBrush(theme.EditorCaretColor);
        caret.Freeze();

        _host.Background = background;
        _host.BorderBrush = accent;
        _editor.Foreground = foreground;
        _editor.CaretBrush = caret;
        _editor.SelectionBrush = accent;
        _editor.FontFamily = theme.CodeFont;
        _editor.FontSize = theme.BaseFontSize;
    }

    /// <summary>
    /// Called after the document re-renders. Block objects are recreated, so the open
    /// editor is re-resolved from its line range before it is repositioned.
    /// </summary>
    public void OnDocumentRendered()
    {
        if (!_isEditing)
            return;

        _viewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (!_isEditing)
                    return;

                if (!TryResolveBand(_startLine, out var rect, out var nextTop, out var endLine))
                {
                    CloseEditor(commit: false);
                    return;
                }

                _endLine = endLine;
                PositionHost(rect, nextTop);
            }));
    }

    /// <summary>
    /// Commits the open edit (when requested) and hides the inline editor.
    /// </summary>
    public void CloseEditor(bool commit)
    {
        if (!_isEditing)
            return;

        var text = _editor.Text;
        var startLine = _startLine;

        _isEditing = false;
        _host.Visibility = Visibility.Collapsed;

        if (commit && !_isCommitting)
            CommitBlock(startLine, text);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _editor.LostKeyboardFocus -= OnEditorLostKeyboardFocus;
        _editor.PreviewKeyDown -= OnEditorPreviewKeyDown;

        _viewer.MouseLeftButtonUp -= OnViewerMouseLeftButtonUp;
        _viewer.SizeChanged -= OnViewerSizeChanged;
        _viewer.Loaded -= OnViewerLoaded;

        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;

        _layer.Children.Remove(_host);
        GC.SuppressFinalize(this);
    }

    #region Input

    private void OnViewerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isCommitting || e.ChangedButton != MouseButton.Left)
            return;

        if (_isEditing && IsWithinHost(e.OriginalSource as DependencyObject))
            return;

        var selection = _viewer.Selection;
        var block = selection is null ? null : FindTopLevelBlock(selection.Start);
        if (block is null || !RenderedBlockMetadata.TryGet(block, out var info))
        {
            CloseEditor(commit: true);
            return;
        }

        if (_isEditing && info.SourceStartLine == _startLine && info.SourceEndLine == _endLine)
            return;

        if (!TryResolveBandForBlock(block, out var rect, out var nextTop))
        {
            CloseEditor(commit: true);
            return;
        }

        if (_isEditing)
            CloseEditor(commit: true);

        BeginEdit(info.SourceStartLine, info.SourceEndLine, rect, nextTop);
    }

    private void OnEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isEditing || _isCommitting || _suppressFocusClose)
            return;

        CloseEditor(commit: true);
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control))
        {
            CloseEditor(commit: true);
            _viewer.Focus();
            e.Handled = true;
        }
    }

    #endregion

    #region Layout

    private void BeginEdit(int startLine, int endLine, Rect rect, double nextTop)
    {
        var markdown = _markdownProvider();
        if (startLine < 1 || endLine < startLine)
            return;

        var start = SourceLineMapper.GetOffsetOfLine(markdown, startLine);
        var end = SourceLineMapper.GetContentEndOfLine(markdown, endLine);
        if (start < 0 || end < start || end > markdown.Length)
            return;

        _startLine = startLine;
        _endLine = endLine;
        _isEditing = true;
        _suppressFocusClose = true;

        _editor.Text = markdown[start..end];
        _host.Visibility = Visibility.Visible;
        PositionHost(rect, nextTop);

        // Focus after the click finishes so the viewer's own focus handling cannot
        // immediately steal it back and close the editor.
        _viewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!_isEditing)
                    return;

                _editor.Focus();
                _editor.CaretIndex = 0;
                _suppressFocusClose = false;
            }));
    }

    private void PositionHost(Rect blockRect, double nextTop)
    {
        var offset = _scrollViewer?.VerticalOffset ?? 0;
        var top = blockRect.Top - offset;
        var height = Math.Max(28, nextTop - blockRect.Top);

        Canvas.SetLeft(_host, 0);
        Canvas.SetTop(_host, top);
        _host.Width = Math.Max(160, _viewer.ActualWidth - 2);
        _host.Height = height;
    }

    /// <summary>
    /// Resolves the block band that contains the given source line.
    /// </summary>
    private bool TryResolveBand(int startLine, out Rect rect, out double nextTop, out int endLine)
    {
        rect = Rect.Empty;
        nextTop = 0;
        endLine = startLine;

        var bands = BuildBands();
        if (bands.Count == 0)
            return false;

        foreach (var band in bands)
        {
            if (band.Info.SourceStartLine != startLine)
                continue;

            rect = band.Rect;
            nextTop = band.NextTop;
            endLine = band.Info.SourceEndLine;
            return true;
        }

        return false;
    }

    private bool TryResolveBandForBlock(WpfBlock target, out Rect rect, out double nextTop)
    {
        rect = Rect.Empty;
        nextTop = 0;

        var bands = BuildBands();
        foreach (var band in bands)
        {
            if (!ReferenceEquals(band.Block, target))
                continue;

            rect = band.Rect;
            nextTop = band.NextTop;
            return true;
        }

        return false;
    }

    private readonly record struct Band(WpfBlock Block, RenderedBlockInfo Info, Rect Rect, double NextTop);

    private List<Band> BuildBands()
    {
        var result = new List<Band>();
        var document = _viewer.Document;
        var scroll = _scrollViewer;
        if (document is null || scroll is null)
            return result;

        var rendered = new List<(WpfBlock Block, RenderedBlockInfo Info, Rect Rect)>();
        foreach (var candidate in document.Blocks)
        {
            if (!RenderedBlockMetadata.TryGet(candidate, out var info))
                continue;

            var rect = candidate.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
                continue;

            rendered.Add((candidate, info, rect));
        }

        for (var i = 0; i < rendered.Count; i++)
        {
            var nextTop = i + 1 < rendered.Count
                ? rendered[i + 1].Rect.Top
                : rendered[i].Rect.Bottom + 16;

            result.Add(new Band(rendered[i].Block, rendered[i].Info, rendered[i].Rect, nextTop));
        }

        return result;
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isEditing && TryResolveBand(_startLine, out var rect, out var nextTop, out _))
            PositionHost(rect, nextTop);
    }

    private void OnViewerLoaded(object sender, RoutedEventArgs e) => AttachScrollViewer();

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindDescendant<ScrollViewer>(_viewer);
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_isEditing)
            return;

        if (TryResolveBand(_startLine, out var rect, out var nextTop, out _))
            PositionHost(rect, nextTop);
    }

    #endregion

    #region Commit

    private void CommitBlock(int startLine, string blockText)
    {
        var markdown = _markdownProvider();
        if (startLine < 1)
            return;

        var start = SourceLineMapper.GetOffsetOfLine(markdown, startLine);
        var end = SourceLineMapper.GetContentEndOfLine(markdown, _endLine);
        if (start < 0 || end < start || end > markdown.Length)
            return;

        var current = markdown[start..end];
        if (string.Equals(current, blockText, StringComparison.Ordinal))
            return;

        _isCommitting = true;
        try
        {
            _applyMarkdown(markdown[..start] + blockText + markdown[end..]);
        }
        finally
        {
            _isCommitting = false;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Opens the block that starts on <paramref name="startLine"/> for editing. Used by
    /// tests to exercise the block-editing path without synthesising mouse input.
    /// </summary>
    internal bool TryOpenBlockForTest(int startLine)
    {
        AttachScrollViewer();
        if (!TryResolveBand(startLine, out var rect, out var nextTop, out var endLine))
            return false;

        BeginEdit(startLine, endLine, rect, nextTop);
        return _isEditing;
    }

    internal string EditorTextForTest => _editor.Text;

    internal void SetEditorTextForTest(string text) => _editor.Text = text;

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

    private bool IsWithinHost(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, _host) || ReferenceEquals(source, _editor))
                return true;

            source = source switch
            {
                Visual => VisualTreeHelper.GetParent(source),
                FrameworkContentElement element => element.Parent,
                _ => LogicalTreeHelper.GetParent(source),
            };
        }

        return false;
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

    #endregion
}