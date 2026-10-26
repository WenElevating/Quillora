using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Rendering;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Table-level selection layer for the Grid-based preview table. It maps mouse
/// gestures to a row-major range of cell <see cref="RichTextBox"/> selections and
/// exposes a plain-text copy path that can span cells.
/// </summary>
internal sealed class TableSelectionController : IDisposable
{
    private static readonly object CopySelectionItemTag = new();
    private static readonly TimeSpan SelectionUpdateInterval = TimeSpan.FromMilliseconds(16);
    private static WeakReference<TableSelectionController>? _activeController;

    private readonly Border _tableRoot;
    private readonly List<TableCellView> _cells;
    private readonly List<TableCellView> _selectedCells = [];
    private readonly Dictionary<RichTextBox, TableCellView> _cellByEditor = [];
    private readonly Dictionary<Border, TableCellView> _cellByContainer = [];
    private readonly Dictionary<TableCellView, Border> _containerByCell = [];
    private readonly Dictionary<TableCellView, Rect> _cellRects = [];
    private readonly Dictionary<TableCellView, AppliedRange> _appliedRanges = [];
    // Mouse moves only record the newest point; selection writes run at most once
    // per UI frame. Reapplying a RichTextBox selection for every mouse event is
    // the main source of drag lag on larger tables.
    private readonly DispatcherTimer _selectionTimer;
    private readonly Canvas? _selectionOverlay;
    private readonly Brush _selectionOverlayBrush;

    private TableCellView? _anchorCell;
    private TextPointer? _anchorPosition;
    private TableCellView? _caretCell;
    private TextPointer? _caretPosition;

    private TableCellView? _dragCandidateCell;
    private Point _dragOrigin;
    private Point _pendingPoint;
    private bool _hasPendingPoint;
    private TableCellView? _lastHitCell;
    private Rect _tableRect = Rect.Empty;
    private ScrollViewer? _previewScrollViewer;
    private bool _dragging;
    private bool _mouseCaptured;
    private bool _completingDrag;
    private bool _disposed;

    private readonly record struct AppliedRange(int StartOffset, int EndOffset, bool WholeCell);

    public TableSelectionController(Border tableRoot, IReadOnlyList<TableCellView> cells)
    {
        _tableRoot = tableRoot;
        _cells = cells.OrderBy(static cell => cell.Row).ThenBy(static cell => cell.Column).ToList();
        _tableRoot.Tag = this;

        foreach (var cell in _cells)
        {
            _cellByEditor[cell.Editor] = cell;
            if (VisualTreeHelper.GetParent(cell.Editor) is Border container)
            {
                _cellByContainer[container] = cell;
                _containerByCell[cell] = container;
            }
        }

        _selectionOverlayBrush = EditorBrushCache.Get(
            Color.FromArgb(0x38, 0x41, 0x83, 0xC4));
        if (tableRoot.Child is Grid grid)
        {
            _selectionOverlay = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetRowSpan(_selectionOverlay, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumnSpan(_selectionOverlay, Math.Max(1, grid.ColumnDefinitions.Count));
            grid.Children.Add(_selectionOverlay);
        }

        _selectionTimer = new DispatcherTimer(DispatcherPriority.Input, tableRoot.Dispatcher)
        {
            Interval = SelectionUpdateInterval,
        };
        _selectionTimer.Tick += OnSelectionTimerTick;

        _tableRoot.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        _tableRoot.AddHandler(
            Mouse.PreviewMouseMoveEvent,
            new MouseEventHandler(OnPreviewMouseMove),
            handledEventsToo: true);
        _tableRoot.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp),
            handledEventsToo: true);
        _tableRoot.AddHandler(
            Mouse.LostMouseCaptureEvent,
            new MouseEventHandler(OnLostMouseCapture),
            handledEventsToo: true);
        _tableRoot.PreviewKeyDown += OnPreviewKeyDown;
        _tableRoot.ContextMenuOpening += OnContextMenuOpening;
        _tableRoot.Unloaded += OnUnloaded;

        foreach (var cell in _cells)
        {
            cell.Editor.Tag = this;
            cell.Editor.ContextMenuOpening += OnContextMenuOpening;
        }
    }

    internal IReadOnlyList<TableCellView> Cells => _cells;

    internal bool HasSelection => _selectedCells.Count > 0;

    internal bool HasCrossCellSelection => _selectedCells.Count > 1;

    internal Rect TableRect => _tableRect;

    internal void RebuildGeometryForTest() => RebuildGeometry();

    internal TableCellView? ResolveCellForTest(Point point) => ResolveCell(point);

    internal Rect GetCellRectForTest(TableCellView cell) =>
        _cellRects.TryGetValue(cell, out var rect) ? rect : Rect.Empty;

    internal void SelectFromPointsForTest(Point anchorPoint, Point caretPoint)
    {
        if (_cellRects.Count == 0)
            RebuildGeometry();

        var anchorCell = ResolveCell(anchorPoint);
        var caretCell = ResolveCell(caretPoint);
        if (anchorCell is null || caretCell is null)
            return;

        _anchorCell = anchorCell;
        _anchorPosition = GetPosition(anchorCell, anchorPoint);
        _caretCell = caretCell;
        _caretPosition = GetPosition(caretCell, caretPoint);
        UpdateSelection();
    }

    internal static bool IsTableSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Border border && border.Tag is TableSelectionController)
                return true;

            if (source is RichTextBox editor && editor.Tag is TableSelectionController)
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

    internal void SelectRange(
        TableCellView startCell,
        TextPointer startPosition,
        TableCellView endCell,
        TextPointer endPosition)
    {
        _anchorCell = startCell;
        _anchorPosition = startPosition;
        _caretCell = endCell;
        _caretPosition = endPosition;
        UpdateSelection();
    }

    /// <summary>Selects whole cells by their row-major indexes. Used by tests and keyboard commands.</summary>
    internal void SelectCells(int startIndex, int endIndex)
    {
        if (_cells.Count == 0)
            return;

        var start = Math.Clamp(Math.Min(startIndex, endIndex), 0, _cells.Count - 1);
        var end = Math.Clamp(Math.Max(startIndex, endIndex), 0, _cells.Count - 1);
        SelectRange(
            _cells[start],
            _cells[start].Editor.Document.ContentStart,
            _cells[end],
            _cells[end].Editor.Document.ContentEnd);
    }

    internal string GetSelectionText()
    {
        if (_selectedCells.Count == 0)
            return string.Empty;

        var builder = new System.Text.StringBuilder();
        TableCellView? previous = null;

        foreach (var cell in _selectedCells)
        {
            if (previous is not null)
                builder.Append(cell.Row == previous.Row ? '\t' : Environment.NewLine);

            builder.Append(cell.Editor.Selection.Text.TrimEnd('\r', '\n'));
            previous = cell;
        }

        return builder.ToString();
    }

    internal void CopySelectionToClipboard()
    {
        var text = GetSelectionText();
        if (text.Length == 0)
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // Another process owns the clipboard; keep the preview responsive.
        }
    }

    internal void ClearSelection()
    {
        _selectionTimer.Stop();
        _hasPendingPoint = false;
        _appliedRanges.Clear();
        _lastHitCell = null;

        foreach (var cell in _cells)
            ClearCellSelection(cell);

        _selectedCells.Clear();
        _selectionOverlay?.Children.Clear();
        _anchorCell = null;
        _anchorPosition = null;
        _caretCell = null;
        _caretPosition = null;
        _dragCandidateCell = null;
        _dragging = false;

        if (_mouseCaptured)
        {
            _mouseCaptured = false;
            _tableRoot.ReleaseMouseCapture();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _selectionTimer.Stop();
        _selectionTimer.Tick -= OnSelectionTimerTick;
        ClearSelection();
        _tableRoot.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown));
        _tableRoot.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnPreviewMouseMove));
        _tableRoot.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp));
        _tableRoot.RemoveHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
        _tableRoot.PreviewKeyDown -= OnPreviewKeyDown;
        _tableRoot.ContextMenuOpening -= OnContextMenuOpening;
        _tableRoot.Unloaded -= OnUnloaded;

        foreach (var cell in _cells)
        {
            cell.Editor.ContextMenuOpening -= OnContextMenuOpening;
            cell.Editor.Tag = null;
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsHyperlinkSource(e.OriginalSource as DependencyObject))
            return;

        RebuildGeometry();
        _previewScrollViewer ??= FindAncestor<ScrollViewer>(_tableRoot);

        var point = e.GetPosition(_tableRoot);
        var cell = ResolveCell(point);
        if (cell is null)
            return;

        var position = GetPosition(cell, point);
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (shift && _anchorCell is not null && _anchorPosition is not null)
        {
            Activate();
            _caretCell = cell;
            _caretPosition = position;
            UpdateSelection();
            e.Handled = true;
            return;
        }

        ClearSelection();
        Activate();

        _dragCandidateCell = cell;
        _dragOrigin = point;
        _anchorCell = cell;
        _anchorPosition = position;
        _caretCell = cell;
        _caretPosition = position;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidateCell is null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidateCell = null;
            return;
        }

        var point = e.GetPosition(_tableRoot);
        if (!_dragging
            && Math.Abs(point.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!_dragging)
        {
            _dragging = true;
            _mouseCaptured = _tableRoot.CaptureMouse();
            _previewScrollViewer ??= FindAncestor<ScrollViewer>(_tableRoot);
            RebuildGeometry();
            _dragCandidateCell.Editor.Focus();
            ClearCellSelections();
        }

        _pendingPoint = point;
        _hasPendingPoint = true;
        if (!_selectionTimer.IsEnabled)
            _selectionTimer.Start();

        e.Handled = true;
    }

    internal void FlushPendingSelection()
    {
        if (!_dragging || !_hasPendingPoint)
            return;

        var point = _pendingPoint;
        _hasPendingPoint = false;
        UpdateSelectionAt(point);
    }

    private void OnSelectionTimerTick(object? sender, EventArgs e)
    {
        if (!_dragging)
        {
            _selectionTimer.Stop();
            return;
        }

        var scrolled = TryAutoScroll();
        if (!_hasPendingPoint && !scrolled)
            return;

        var point = scrolled || _hasPendingPoint
            ? Mouse.GetPosition(_tableRoot)
            : _pendingPoint;
        _hasPendingPoint = false;
        UpdateSelectionAt(point);
    }

    private void UpdateSelectionAt(Point point)
    {
        var cell = ResolveCell(point);
        if (cell is null)
            return;

        var position = GetPosition(cell, point);
        _dragCandidateCell = cell;
        _caretCell = cell;
        _caretPosition = position;
        UpdateSelection();
    }

    private bool TryAutoScroll()
    {
        if (_previewScrollViewer is null || !_previewScrollViewer.IsVisible)
            return false;

        var pointer = Mouse.GetPosition(_previewScrollViewer);
        var viewportHeight = _previewScrollViewer.ViewportHeight > 0
            ? _previewScrollViewer.ViewportHeight
            : _previewScrollViewer.ActualHeight;
        if (viewportHeight <= 0)
            return false;

        const double edgeThreshold = 32;
        const double scrollStep = 28;
        double delta;
        if (pointer.Y < edgeThreshold)
            delta = -scrollStep;
        else if (pointer.Y > viewportHeight - edgeThreshold)
            delta = scrollStep;
        else
            return false;

        // Do not scroll the document when the table is already fully visible;
        // dragging below a short table should simply clamp to its last row.
        var tableTop = _tableRoot.TranslatePoint(new Point(0, 0), _previewScrollViewer).Y;
        var tableBottom = tableTop + _tableRoot.ActualHeight;
        if (delta < 0 && tableTop >= 0)
            return false;
        if (delta > 0 && tableBottom <= viewportHeight)
            return false;

        var before = _previewScrollViewer.VerticalOffset;
        var target = Math.Clamp(
            before + delta,
            0,
            _previewScrollViewer.ScrollableHeight);
        if (Math.Abs(target - before) < 0.5)
            return false;

        _previewScrollViewer.ScrollToVerticalOffset(target);
        return true;
    }

    private void CompleteDrag()
    {
        if (_completingDrag || !_dragging)
            return;

        _completingDrag = true;
        try
        {
            FlushPendingSelection();
            _selectionTimer.Stop();
            _dragging = false;
            _mouseCaptured = false;
            _caretCell?.Editor.Focus();
            if (Mouse.Captured == _tableRoot)
                _tableRoot.ReleaseMouseCapture();
        }
        finally
        {
            _completingDrag = false;
        }
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            _dragCandidateCell = null;
            return;
        }

        CompleteDrag();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging)
            CompleteDrag();
        else
        {
            _selectionTimer.Stop();
            _hasPendingPoint = false;
            _mouseCaptured = false;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C
            && Keyboard.Modifiers == ModifierKeys.Control
            && HasCrossCellSelection)
        {
            CopySelectionToClipboard();
            e.Handled = true;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is not { } menu)
            return;

        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is MenuItem item && ReferenceEquals(item.Tag, CopySelectionItemTag))
                menu.Items.RemoveAt(i);
        }

        if (!HasSelection)
            return;

        var copySelection = new MenuItem
        {
            Header = Application.Current?.TryFindResource("Loc.Editor.CopySelection") as string
                ?? "Copy selection",
            Tag = CopySelectionItemTag,
        };
        copySelection.Click += (_, _) => CopySelectionToClipboard();
        menu.Items.Insert(0, copySelection);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => ClearSelection();

    private void UpdateSelection()
    {
        if (_anchorCell is null || _anchorPosition is null || _caretCell is null || _caretPosition is null)
        {
            ClearSelection();
            return;
        }

        var anchorIndex = _cells.IndexOf(_anchorCell);
        var caretIndex = _cells.IndexOf(_caretCell);
        if (anchorIndex < 0 || caretIndex < 0)
            return;

        var startIndex = Math.Min(anchorIndex, caretIndex);
        var endIndex = Math.Max(anchorIndex, caretIndex);
        var startPosition = anchorIndex <= caretIndex ? _anchorPosition : _caretPosition;
        var endPosition = anchorIndex <= caretIndex ? _caretPosition : _anchorPosition;

        var targetCells = new HashSet<TableCellView>();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var cell = _cells[i];
            targetCells.Add(cell);

            // Middle cells are whole-cell selections and are written once. Only
            // the two edge cells can change on every frame.
            if (startIndex == endIndex)
            {
                ApplyRange(cell, startPosition, endPosition);
            }
            else if (i == startIndex)
            {
                ApplyRange(cell, startPosition, cell.Editor.Document.ContentEnd);
            }
            else if (i == endIndex)
            {
                ApplyRange(cell, cell.Editor.Document.ContentStart, endPosition);
            }
            else
            {
                ApplyWholeCell(cell);
            }
        }

        foreach (var cell in _appliedRanges.Keys.ToList())
        {
            if (targetCells.Contains(cell))
                continue;

            ClearCellSelection(cell);
            _appliedRanges.Remove(cell);
        }

        _selectedCells.Clear();
        for (var i = startIndex; i <= endIndex; i++)
            _selectedCells.Add(_cells[i]);

        RenderSelectionOverlay();
    }

    /// <summary>
    /// Inactive RichTextBox selections are not reliably painted by WPF inside a
    /// FlowDocument, so draw a lightweight overlay for every selected cell.
    /// </summary>
    private void RenderSelectionOverlay()
    {
        if (_selectionOverlay is null)
            return;

        _selectionOverlay.Children.Clear();
        foreach (var cell in _selectedCells)
        {
            if (cell.Editor.Selection.IsEmpty || cell.Editor.ActualWidth <= 0 || cell.Editor.ActualHeight <= 0)
                continue;

            var origin = cell.Editor.TranslatePoint(new Point(0, 0), _selectionOverlay);
            var rectangle = new Rectangle
            {
                Width = cell.Editor.ActualWidth,
                Height = cell.Editor.ActualHeight,
                Fill = _selectionOverlayBrush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rectangle, origin.X);
            Canvas.SetTop(rectangle, origin.Y);
            _selectionOverlay.Children.Add(rectangle);
        }
    }

    private void ApplyRange(TableCellView cell, TextPointer start, TextPointer end)
    {
        if (start.CompareTo(end) > 0)
            (start, end) = (end, start);

        var target = new AppliedRange(
            cell.Editor.Document.ContentStart.GetOffsetToPosition(start),
            cell.Editor.Document.ContentStart.GetOffsetToPosition(end),
            WholeCell: false);

        if (_appliedRanges.TryGetValue(cell, out var applied) && applied == target)
            return;

        cell.Editor.Selection.Select(start, end);
        _appliedRanges[cell] = target;
    }

    private void ApplyWholeCell(TableCellView cell)
    {
        var target = new AppliedRange(0, int.MaxValue, WholeCell: true);
        if (_appliedRanges.TryGetValue(cell, out var applied) && applied == target)
            return;

        cell.Editor.SelectAll();
        _appliedRanges[cell] = target;
    }

    private static void ClearCellSelection(TableCellView cell)
    {
        if (cell.Editor.Selection.IsEmpty)
            return;

        var start = cell.Editor.Document.ContentStart;
        cell.Editor.Selection.Select(start, start);
    }

    private void ClearCellSelections()
    {
        foreach (var cell in _cells)
            ClearCellSelection(cell);

        _appliedRanges.Clear();
        _selectedCells.Clear();
        _selectionOverlay?.Children.Clear();
    }

    private void Activate()
    {
        if (_activeController is not null
            && _activeController.TryGetTarget(out var active)
            && !ReferenceEquals(active, this))
        {
            active.ClearSelection();
        }

        _activeController = new WeakReference<TableSelectionController>(this);
    }

    private void RebuildGeometry()
    {
        _cellRects.Clear();
        _tableRect = new Rect(
            new Point(0, 0),
            new Size(Math.Max(0, _tableRoot.ActualWidth), Math.Max(0, _tableRoot.ActualHeight)));

        foreach (var cell in _cells)
        {
            if (!_containerByCell.TryGetValue(cell, out var container))
                continue;

            var topLeft = container.TranslatePoint(new Point(0, 0), _tableRoot);
            _cellRects[cell] = new Rect(
                topLeft,
                new Size(Math.Max(0, container.ActualWidth), Math.Max(0, container.ActualHeight)));
        }
    }

    private TableCellView? ResolveCell(Point point)
    {
        if (_lastHitCell is not null
            && _cellRects.TryGetValue(_lastHitCell, out var cachedRect)
            && cachedRect.Contains(point))
        {
            return _lastHitCell;
        }

        var hit = _tableRoot.InputHitTest(point) as DependencyObject;
        var cell = FindCell(hit);
        if (cell is not null)
        {
            _lastHitCell = cell;
            return cell;
        }

        if (_cellRects.Count == 0)
            RebuildGeometry();

        if (_cellRects.Count == 0 || _tableRect.IsEmpty || _tableRect.Width <= 0 || _tableRect.Height <= 0)
            return null;

        // Clamp points outside the table (or in border/padding gaps) to the
        // nearest cell so dragging past the last row/column still reaches it.
        var clamped = new Point(
            Math.Clamp(point.X, _tableRect.Left + 0.5, _tableRect.Right - 0.5),
            Math.Clamp(point.Y, _tableRect.Top + 0.5, _tableRect.Bottom - 0.5));

        TableCellView? nearest = null;
        var nearestDistance = double.MaxValue;
        foreach (var (candidate, rect) in _cellRects)
        {
            if (rect.Contains(clamped))
            {
                _lastHitCell = candidate;
                return candidate;
            }

            var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            var distance = (center - clamped).LengthSquared;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        _lastHitCell = nearest;
        return nearest;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;

            source = source switch
            {
                Visual => VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(source),
            };
        }

        return null;
    }

    private TableCellView? FindCell(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is RichTextBox editor && _cellByEditor.TryGetValue(editor, out var editorCell))
                return editorCell;

            if (source is Border border && _cellByContainer.TryGetValue(border, out var containerCell))
                return containerCell;

            source = source switch
            {
                Visual => VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(source),
            };
        }

        return null;
    }

    private TextPointer GetPosition(TableCellView cell, Point pointInTable)
    {
        var point = _tableRoot.TranslatePoint(pointInTable, cell.Editor);
        return cell.Editor.GetPositionFromPoint(point, snapToText: true)
            ?? (point.Y >= cell.Editor.ActualHeight / 2
                ? cell.Editor.Document.ContentEnd
                : cell.Editor.Document.ContentStart);
    }

    private static bool IsHyperlinkSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Hyperlink)
                return true;

            source = source switch
            {
                Visual => VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(source),
            };
        }

        return false;
    }
}

internal sealed record TableCellView(int Row, int Column, RichTextBox Editor);
