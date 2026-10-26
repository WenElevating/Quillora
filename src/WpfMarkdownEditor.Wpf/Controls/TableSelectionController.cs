using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
    private readonly Dictionary<TableCellView, AppliedRange> _appliedRanges = [];
    // Mouse moves only record the newest point; selection writes run at most once
    // per UI frame. Reapplying a RichTextBox selection for every mouse event is
    // the main source of drag lag on larger tables.
    private readonly DispatcherTimer _selectionTimer;

    private TableCellView? _anchorCell;
    private TextPointer? _anchorPosition;
    private TableCellView? _caretCell;
    private TextPointer? _caretPosition;

    private TableCellView? _dragCandidateCell;
    private Point _dragOrigin;
    private Point _pendingPoint;
    private bool _hasPendingPoint;
    private TableCellView? _lastHitCell;
    private Rect _lastHitCellBounds;
    private bool _dragging;
    private bool _mouseCaptured;
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
                _cellByContainer[container] = cell;
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

        var point = e.GetPosition(_tableRoot);
        var cell = HitTestCell(point);
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
        if (!_dragging || !_hasPendingPoint)
        {
            _selectionTimer.Stop();
            return;
        }

        var point = _pendingPoint;
        _hasPendingPoint = false;
        UpdateSelectionAt(point);
    }

    private void UpdateSelectionAt(Point point)
    {
        var cell = HitTestCell(point) ?? _lastHitCell ?? _dragCandidateCell;
        if (cell is null)
            return;

        var position = GetPosition(cell, point);
        _dragCandidateCell = cell;
        _caretCell = cell;
        _caretPosition = position;
        UpdateSelection();
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            _dragCandidateCell = null;
            return;
        }

        FlushPendingSelection();
        _selectionTimer.Stop();
        _dragging = false;
        _mouseCaptured = false;
        _caretCell?.Editor.Focus();
        _tableRoot.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _selectionTimer.Stop();
        _hasPendingPoint = false;
        _mouseCaptured = false;
        _dragging = false;
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

    private TableCellView? HitTestCell(Point point)
    {
        if (_lastHitCell is not null && _lastHitCellBounds.Contains(point))
            return _lastHitCell;

        var hit = _tableRoot.InputHitTest(point) as DependencyObject;
        var cell = FindCell(hit);
        if (cell is null)
            return null;

        _lastHitCell = cell;
        var topLeft = cell.Editor.TranslatePoint(new Point(0, 0), _tableRoot);
        _lastHitCellBounds = new Rect(
            topLeft,
            new Size(cell.Editor.ActualWidth, cell.Editor.ActualHeight));
        return cell;
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
