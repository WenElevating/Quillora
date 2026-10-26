using System.Runtime.InteropServices;
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
            ContextMenu = CreateTableContextMenu(table, cellText: null),
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

            var paragraph = new Paragraph
            {
                FontFamily = theme.BodyFont,
                FontSize = theme.BaseFontSize,
                Foreground = EditorBrushCache.Get(theme.ForegroundColor),
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = GetAlignment(table, column),
                LineHeight = TypographyScale.ResolveBodyLineHeight(theme),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };
            _inlineRenderer.RenderInlines(paragraph, _markdownParser.ParseInlines(text));

            var document = new FlowDocument(paragraph)
            {
                PagePadding = new Thickness(0),
                FontFamily = theme.BodyFont,
                FontSize = theme.BaseFontSize,
                Foreground = EditorBrushCache.Get(theme.ForegroundColor),
                LineHeight = TypographyScale.ResolveBodyLineHeight(theme),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };

            var cellContent = new RichTextBox
            {
                Document = document,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                IsDocumentEnabled = true,
                IsUndoEnabled = false,
                AcceptsTab = false,
                IsTabStop = false,
                CaretBrush = Brushes.Transparent,
                SelectionBrush = EditorBrushCache.Get(theme.LinkColor),
                SelectionOpacity = 0.25,
                IsInactiveSelectionHighlightEnabled = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                ContextMenu = CreateTableContextMenu(table, text),
            };

            var cell = new Border
            {
                Background = isHeader
                    ? EditorBrushCache.Get(theme.TableHeaderBackground)
                    : rowIndex % 2 == 1
                        ? EditorBrushCache.Get(theme.TableAltRowBackground)
                        : Brushes.Transparent,
                BorderBrush = EditorBrushCache.Get(theme.TableBorderColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 6, 8, 6),
                Child = cellContent,
            };

            Grid.SetRow(cell, gridRow);
            Grid.SetColumn(cell, column);
            if (column == flexibleColumn)
            {
                cell.MinWidth = 120;
            }
            else
            {
                cell.Width = EstimateCellWidth(table, column);
            }

            grid.Children.Add(cell);
        }
    }

    private double EstimateCellWidth(TableBlock table, int column)
    {
        var longestWord = 0;
        void Measure(string text)
        {
            foreach (var word in text.Split(' ', '\t', '|'))
                longestWord = Math.Max(longestWord, CountDisplayWidth(word));
        }

        if (column < table.Headers.Count)
            Measure(table.Headers[column]);
        foreach (var row in table.Rows)
        {
            if (column < row.Count)
                Measure(row[column]);
        }

        // 0.55em per Latin character + left/right padding + cell borders.
        return Math.Max(64, longestWord * theme.BaseFontSize * 0.55 + 26);
    }

    private static int CountDisplayWidth(string text)
    {
        var width = 0;
        foreach (var character in text)
            width += character >= 0x2E80 ? 2 : 1;
        return width;
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

    private static ContextMenu CreateTableContextMenu(TableBlock table, string? cellText)
    {
        var menu = new ContextMenu();

        if (cellText is not null)
            menu.Items.Add(CreateMenuItem("Loc.Editor.CopyCell", "Copy cell", () => CopyText(cellText)));

        menu.Items.Add(CreateMenuItem("Loc.Editor.CopyTable", "Copy table", () => CopyText(BuildTableText(table))));
        return menu;
    }

    private static MenuItem CreateMenuItem(string resourceKey, string fallback, Action action)
    {
        var item = new MenuItem
        {
            Header = Application.Current?.TryFindResource(resourceKey) as string ?? fallback,
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // Another process owns the clipboard; do not crash the preview.
        }
    }

    private static string BuildTableText(TableBlock table)
    {
        var lines = new List<string> { FormatRow(table.Headers) };
        lines.AddRange(table.Rows.Select(FormatRow));
        return string.Join(Environment.NewLine, lines);

        static string FormatRow(IReadOnlyList<string> cells) =>
            string.Join("\t", cells.Select(static cell => cell.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')));
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
