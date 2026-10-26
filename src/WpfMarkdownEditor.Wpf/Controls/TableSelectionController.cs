using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private static WeakReference<TableSelectionController>? _activeController;

    private readonly Border _tableRoot;
    private readonly List<TableCellView> _cells;
    private readonly List<TableCellView> _selectedCells = [];

    private TableCellView? _anchorCell;
    private TextPointer? _anchorPosition;
    private TableCellView? _caretCell;
    private TextPointer? _caretPosition;

    private TableCellView? _dragCandidateCell;
    private Point _dragOrigin;
    private bool _dragging;
    private bool _mouseCaptured;
    private bool _disposed;

    public TableSelectionController(Border tableRoot, IReadOnlyList<TableCellView> cells)
    {
        _tableRoot = tableRoot;
        _cells = cells.OrderBy(static cell => cell.Row).ThenBy(static cell => cell.Column).ToList();
        _tableRoot.Tag = this;

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
        ClearSelection();
        _tableRoot.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown));
        _tableRoot.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnPreviewMouseMove));
        _tableRoot.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp));
        _tableRoot.RemoveHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
        _tableRoot.PreviewKeyDown -= OnPreviewKeyDown;
        _tableRoot.ContextMenuOpening -= OnContextMenuOpening;
        _tableRoot.Unloaded -= OnUnloaded;

        foreach (var cell in _cells)
            cell.Editor.ContextMenuOpening -= OnContextMenuOpening;
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

        var cell = HitTestCell(point) ?? _dragCandidateCell;
        var position = GetPosition(cell, point);
        _dragCandidateCell = cell;
        _caretCell = cell;
        _caretPosition = position;
        UpdateSelection();
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            _dragCandidateCell = null;
            return;
        }

        _dragging = false;
        _mouseCaptured = false;
        _caretCell?.Editor.Focus();
        _tableRoot.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
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
        var startCell = _cells[startIndex];
        var endCell = _cells[endIndex];
        var startPosition = anchorIndex <= caretIndex ? _anchorPosition : _caretPosition;
        var endPosition = anchorIndex <= caretIndex ? _caretPosition : _anchorPosition;

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (i < startIndex || i > endIndex)
            {
                ClearCellSelection(cell);
                continue;
            }

            if (startIndex == endIndex)
            {
                SelectInCell(cell.Editor, startPosition, endPosition);
            }
            else if (i == startIndex)
            {
                SelectInCell(cell.Editor, startPosition, cell.Editor.Document.ContentEnd);
            }
            else if (i == endIndex)
            {
                SelectInCell(cell.Editor, cell.Editor.Document.ContentStart, endPosition);
            }
            else
            {
                cell.Editor.SelectAll();
            }
        }

        _selectedCells.Clear();
        for (var i = startIndex; i <= endIndex; i++)
            _selectedCells.Add(_cells[i]);
    }

    private static void SelectInCell(RichTextBox editor, TextPointer start, TextPointer end)
    {
        if (start.CompareTo(end) > 0)
            (start, end) = (end, start);

        editor.Selection.Select(start, end);
    }

    private static void ClearCellSelection(TableCellView cell)
    {
        var start = cell.Editor.Document.ContentStart;
        cell.Editor.Selection.Select(start, start);
    }

    private void ClearCellSelections()
    {
        foreach (var cell in _cells)
            ClearCellSelection(cell);

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
        var hit = _tableRoot.InputHitTest(point) as DependencyObject;
        var cell = FindCell(hit);
        if (cell is not null)
            return cell;

        foreach (var candidate in _cells)
        {
            var topLeft = candidate.Editor.TranslatePoint(new Point(0, 0), _tableRoot);
            var width = candidate.Editor.ActualWidth;
            var height = candidate.Editor.ActualHeight;
            if (point.X >= topLeft.X && point.X <= topLeft.X + width
                && point.Y >= topLeft.Y && point.Y <= topLeft.Y + height)
            {
                return candidate;
            }
        }

        return null;
    }

    private TableCellView? FindCell(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is RichTextBox editor)
                return _cells.FirstOrDefault(cell => ReferenceEquals(cell.Editor, editor));

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
