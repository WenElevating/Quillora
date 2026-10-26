using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

/// <summary>
/// Renders GFM tables with a WPF Grid inside a BlockUIContainer. A Grid gives us
/// content-sized compact columns plus a flexible prose column, which a FlowDocument
/// Table cannot express reliably alongside cell wrapping and vertical centering.
/// </summary>
public sealed class TableRenderer(
    EditorTheme theme,
    IImageResolver? imageResolver = null,
    Action? requestLayoutRefresh = null) : IBlockRenderer
{
    private readonly InlineRenderer _inlineRenderer = new(theme, imageResolver, requestLayoutRefresh);
    private readonly MarkdownParser _markdownParser = new();

    public System.Windows.Documents.Block Render(Core.Parsing.Block block)
    {
        var table = (TableBlock)block;
        var columnCount = Math.Max(table.Headers.Count,
            table.Rows.Count > 0 ? table.Rows.Max(static row => row.Count) : 0);

        if (columnCount == 0)
            return new BlockUIContainer();

        var flexibleColumn = FindFlexibleColumn(table, columnCount);

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var i = 0; i < columnCount; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == flexibleColumn
                    ? new GridLength(1, GridUnitType.Star)
                    : GridLength.Auto,
            });
        }

        AddRow(grid, table, 0, isHeader: true, columnCount, flexibleColumn);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            AddRow(grid, table, rowIndex, isHeader: false, columnCount, flexibleColumn);

        var border = new Border
        {
            BorderBrush = EditorBrushCache.Get(theme.TableBorderColor),
            BorderThickness = new Thickness(1),
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        return new BlockUIContainer(border)
        {
            Margin = new Thickness(0, 0, 0, TypographyScale.BlockSpacing(theme)),
        };
    }

    private void AddRow(
        Grid grid,
        TableBlock table,
        int rowIndex,
        bool isHeader,
        int columnCount,
        int flexibleColumn)
    {
        var row = new RowDefinition { Height = GridLength.Auto };
        grid.RowDefinitions.Add(row);
        var gridRow = isHeader ? 0 : rowIndex + 1;

        for (var column = 0; column < columnCount; column++)
        {
            var text = isHeader
                ? column < table.Headers.Count ? table.Headers[column] : string.Empty
                : column < table.Rows[rowIndex].Count ? table.Rows[rowIndex][column] : string.Empty;

            var textBlock = new TextBlock
            {
                FontFamily = theme.BodyFont,
                FontSize = theme.BaseFontSize,
                Foreground = EditorBrushCache.Get(theme.ForegroundColor),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = GetAlignment(table, column),
                VerticalAlignment = VerticalAlignment.Center,
                LineHeight = TypographyScale.ResolveBodyLineHeight(theme),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };

            if (isHeader)
                textBlock.FontWeight = FontWeights.Bold;

            _inlineRenderer.RenderInlines(textBlock, _markdownParser.ParseInlines(text));

            var cell = new Border
            {
                Background = isHeader
                    ? EditorBrushCache.Get(theme.TableHeaderBackground)
                    : rowIndex % 2 == 1
                        ? EditorBrushCache.Get(theme.TableAltRowBackground)
                        : Brushes.Transparent,
                BorderBrush = EditorBrushCache.Get(theme.TableBorderColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(13, 6, 13, 6),
                Child = textBlock,
            };

            Grid.SetRow(cell, gridRow);
            Grid.SetColumn(cell, column);
            if (column == flexibleColumn)
                cell.MinWidth = 120;

            grid.Children.Add(cell);
        }
    }

    private static int FindFlexibleColumn(TableBlock table, int columnCount)
    {
        var flexibleColumn = 0;
        var bestLength = -1;

        for (var column = 0; column < columnCount; column++)
        {
            var length = column < table.Headers.Count ? table.Headers[column].Length : 0;
            foreach (var row in table.Rows)
            {
                if (column < row.Count)
                    length = Math.Max(length, row[column].Length);
            }

            if (length > bestLength)
            {
                bestLength = length;
                flexibleColumn = column;
            }
        }

        return flexibleColumn;
    }

    private static TextAlignment GetAlignment(TableBlock table, int column) =>
        column < table.Alignments.Count
            ? table.Alignments[column] switch
            {
                TableBlock.TableAlignment.Center => TextAlignment.Center,
                TableBlock.TableAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            }
            : TextAlignment.Left;
}
