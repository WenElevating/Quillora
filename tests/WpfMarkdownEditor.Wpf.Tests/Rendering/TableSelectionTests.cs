using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class TableSelectionTests
{
    private readonly MarkdownParser _parser = new();
    private readonly FlowDocumentRenderer _renderer = new(EditorTheme.GitHub);

    [Fact]
    public void SelectCells_AcrossRows_SelectsEveryCellAndBuildsTabText()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");

            controller.SelectCells(0, 3);

            Assert.Equal(4, controller.Cells.Count);
            Assert.All(controller.Cells, cell => Assert.False(cell.Editor.Selection.IsEmpty));
            Assert.Equal($"A\tB{Environment.NewLine}1\t2", controller.GetSelectionText());
        });
    }

    [Fact]
    public void SelectRange_ReverseDirection_NormalizesRowMajorOrder()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");
            var cells = controller.Cells;
            controller.SelectRange(
                cells[3],
                cells[3].Editor.Document.ContentEnd,
                cells[0],
                cells[0].Editor.Document.ContentStart);

            Assert.Equal($"A\tB{Environment.NewLine}1\t2", controller.GetSelectionText());
        });
    }

    [Fact]
    public void SelectRange_WithinOneCell_KeepsOnlyThatCellSelected()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");
            var cells = controller.Cells;
            controller.SelectRange(
                cells[0],
                cells[0].Editor.Document.ContentStart,
                cells[0],
                cells[0].Editor.Document.ContentEnd);

            Assert.True(cells[0].Editor.Selection.IsEmpty == false);
            Assert.True(cells[1].Editor.Selection.IsEmpty);
            Assert.True(cells[2].Editor.Selection.IsEmpty);
            Assert.True(cells[3].Editor.Selection.IsEmpty);
            Assert.Equal("A", controller.GetSelectionText());
        });
    }

    [Fact]
    public void RepeatedRangeUpdates_ShrinkAndExpandWithoutStaleSelections()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");
            var cells = controller.Cells;

            controller.SelectCells(0, 3);
            controller.SelectCells(0, 1);

            Assert.False(cells[0].Editor.Selection.IsEmpty);
            Assert.False(cells[1].Editor.Selection.IsEmpty);
            Assert.True(cells[2].Editor.Selection.IsEmpty);
            Assert.True(cells[3].Editor.Selection.IsEmpty);
            Assert.Equal($"A\tB", controller.GetSelectionText());

            controller.SelectCells(2, 3);
            Assert.True(cells[0].Editor.Selection.IsEmpty);
            Assert.True(cells[1].Editor.Selection.IsEmpty);
            Assert.False(cells[2].Editor.Selection.IsEmpty);
            Assert.False(cells[3].Editor.Selection.IsEmpty);
            Assert.Equal($"1\t2", controller.GetSelectionText());
        });
    }

    [Fact]
    public void RepeatedSameRange_DoesNotReapplySelection()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");
            var changes = 0;
            foreach (var cell in controller.Cells)
                cell.Editor.SelectionChanged += (_, _) => changes++;

            controller.SelectCells(0, 3);
            changes = 0;
            controller.SelectCells(0, 3);

            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void ResolveCell_OutsideRightAndBottom_ClampsToLastCell()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.RebuildGeometryForTest();
                var lastCell = controller.Cells[^1];
                var tableRect = controller.TableRect;

                var resolved = controller.ResolveCellForTest(
                    new Point(tableRect.Right + 80, tableRect.Bottom + 80));

                Assert.Same(lastCell, resolved);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ResolveCell_OutsideTopLeft_ClampsToFirstCell()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.RebuildGeometryForTest();
                var firstCell = controller.Cells[0];
                var tableRect = controller.TableRect;

                var resolved = controller.ResolveCellForTest(
                    new Point(tableRect.Left - 80, tableRect.Top - 80));

                Assert.Same(firstCell, resolved);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TableSelection_HidesNativeHighlightAndRestoresItAfterClear()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                var originalOpacity = controller.Cells[0].Editor.SelectionOpacity;

                controller.SelectCells(0, controller.Cells.Count - 1);
                Assert.All(controller.Cells, cell => Assert.Equal(0, cell.Editor.SelectionOpacity));

                controller.ClearSelection();
                Assert.All(controller.Cells, cell =>
                    Assert.Equal(originalOpacity, cell.Editor.SelectionOpacity));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SelectFromPoints_FirstCellToLastCell_SelectsEveryCell()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.RebuildGeometryForTest();
                var first = controller.GetCellRectForTest(controller.Cells[0]);
                var last = controller.GetCellRectForTest(controller.Cells[^1]);
                var anchor = new Point(first.Left + 4, first.Top + first.Height / 2);
                var caret = new Point(last.Right - 4, last.Top + last.Height / 2);

                controller.SelectFromPointsForTest(anchor, caret);

                Assert.All(controller.Cells, cell => Assert.False(cell.Editor.Selection.IsEmpty));
                Assert.Equal(
                    $"A\tB{Environment.NewLine}1\t2",
                    controller.GetSelectionText());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SelectionOverlay_HighlightsEverySelectedCell()
    {
        WpfTestHost.Run(() =>
        {
            var (border, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                var grid = Assert.IsType<Grid>(border.Child);
                var overlay = Assert.Single(grid.Children.OfType<Canvas>());

                controller.SelectCells(0, controller.Cells.Count - 1);

                Assert.Equal(controller.Cells.Count, overlay.Children.Count);

                controller.ClearSelection();
                Assert.Empty(overlay.Children);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ResolveCell_EveryCellCenter_ReturnsThatCell()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.RebuildGeometryForTest();

                foreach (var cell in controller.Cells)
                {
                    var rect = controller.GetCellRectForTest(cell);
                    var center = new Point(
                        rect.Left + rect.Width / 2,
                        rect.Top + rect.Height / 2);

                    Assert.Same(cell, controller.ResolveCellForTest(center));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ClearSelection_ResetsEveryCellAndText()
    {
        WpfTestHost.Run(() =>
        {
            var (_, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");
            controller.SelectCells(0, 3);

            controller.ClearSelection();

            Assert.False(controller.HasSelection);
            Assert.Equal(string.Empty, controller.GetSelectionText());
            Assert.All(controller.Cells, cell => Assert.True(cell.Editor.Selection.IsEmpty));
        });
    }

    [Fact]
    public void Renderer_AttachesControllerToTableBorder()
    {
        WpfTestHost.Run(() =>
        {
            var (border, controller) = RenderTable("| A | B |\n| --- | --- |\n| 1 | 2 |");

            Assert.Same(controller, border.Tag);
            Assert.True(TableSelectionController.IsTableSource(controller.Cells[0].Editor));

            var paragraph = Assert.IsType<Paragraph>(
                controller.Cells[0].Editor.Document.Blocks.First());
            Assert.True(TableSelectionController.IsTableSource(paragraph));
        });
    }

    [Fact]
    public void ClearSelection_WhenKeyboardFocusLeavesTheTable()
    {
        WpfTestHost.Run(() =>
        {
            var (border, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.SelectCells(0, 3);
                // A real selection always comes from a click inside a cell, which
                // focuses that cell's editor. Establish the same state first.
                controller.Cells[0].Editor.Focus();
                window.UpdateLayout();
                Assert.True(controller.HasSelection);

                // Simulate clicking outside the table: focus moves to another element
                // and the table keeps no keyboard focus anymore. The table border
                // belongs to the document, so re-parent the viewer instead.
                var outside = new TextBox();
                var viewer = (FlowDocumentScrollViewer)window.Content;
                window.Content = null;
                var dock = new DockPanel();
                DockPanel.SetDock(outside, Dock.Top);
                dock.Children.Add(outside);
                dock.Children.Add(viewer);
                window.Content = dock;
                window.UpdateLayout();

                outside.Focus();
                window.UpdateLayout();

                // In an inactive test window keyboard focus stays wherever the OS put
                // it; the meaningful check is where the window's logical focus sits.
                Assert.False(controller.Cells[0].Editor.IsFocused);
                Assert.False(controller.HasSelection);
                Assert.All(controller.Cells, cell => Assert.True(cell.Editor.Selection.IsEmpty));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Selection_SurvivesFocusMovingInsideTheTable()
    {
        WpfTestHost.Run(() =>
        {
            var (border, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.SelectCells(0, 3);

                // Focus moving between the table's own editors must not clear it.
                controller.Cells[3].Editor.Focus();
                window.UpdateLayout();

                Assert.True(controller.HasSelection);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ClearSelection_WhenMouseClicksOutsideTheTable()
    {
        WpfTestHost.Run(() =>
        {
            var (border, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.SelectCells(0, 3);
                controller.Cells[0].Editor.Focus();
                window.UpdateLayout();
                Assert.True(controller.HasSelection);

                // The window-level dismiss hook must clear the selection when a
                // left click lands outside the table border. Raise the same routed
                // event the OS would deliver for a real click on the window.
                var click = new MouseButtonEventArgs(
                    InputManager.Current.PrimaryMouseDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = window,
                };
                window.RaiseEvent(click);

                Assert.False(controller.HasSelection);
                Assert.All(controller.Cells, cell => Assert.True(cell.Editor.Selection.IsEmpty));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ClearSelection_IgnoresRightButtonOutsideTheTable()
    {
        WpfTestHost.Run(() =>
        {
            var (border, controller, window) = RenderTableInWindow(
                "| A | B |\n| --- | --- |\n| 1 | 2 |");
            try
            {
                controller.SelectCells(0, 3);
                controller.Cells[0].Editor.Focus();
                window.UpdateLayout();
                Assert.True(controller.HasSelection);

                // Right click outside must keep the selection so the context menu
                // stays usable on the selection itself.
                var click = new MouseButtonEventArgs(
                    InputManager.Current.PrimaryMouseDevice,
                    Environment.TickCount,
                    MouseButton.Right)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = window,
                };
                window.RaiseEvent(click);

                Assert.True(controller.HasSelection);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private (Border Border, TableSelectionController Controller) RenderTable(string markdown)
    {
        var document = _renderer.Render(_parser.Parse(markdown));
        var container = Assert.IsType<BlockUIContainer>(document.Blocks.First());
        var border = Assert.IsType<Border>(container.Child);
        var controller = Assert.IsType<TableSelectionController>(border.Tag);
        return (border, controller);
    }

    private (Border Border, TableSelectionController Controller, Window Window) RenderTableInWindow(string markdown)
    {
        var document = _renderer.Render(_parser.Parse(markdown));
        var container = Assert.IsType<BlockUIContainer>(document.Blocks.First());
        var border = Assert.IsType<Border>(container.Child);
        var controller = Assert.IsType<TableSelectionController>(border.Tag);
        var viewer = new FlowDocumentScrollViewer
        {
            Document = document,
            Width = 560,
            Height = 420,
        };
        var window = new Window { Content = viewer, Width = 620, Height = 500 };
        window.Show();
        window.UpdateLayout();
        return (border, controller, window);
    }
}
