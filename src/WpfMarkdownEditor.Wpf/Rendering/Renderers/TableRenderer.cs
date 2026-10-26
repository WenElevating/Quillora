using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering.Renderers;

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
        var tableEl = new Table
        {
            Margin = new Thickness(0, 0, 0, TypographyScale.BlockSpacing(theme)),
            BorderBrush = EditorBrushCache.Get(theme.TableBorderColor),
            BorderThickness = new Thickness(1),
            CellSpacing = 0,
        };

        var columns = Math.Max(table.Headers.Count,
            table.Rows.Count > 0 ? table.Rows.Max(r => r.Count) : 0);

        for (var i = 0; i < columns; i++)
            tableEl.Columns.Add(new TableColumn());

        var rg = new TableRowGroup();

        // Header row
        var headerRow = new TableRow { Background = EditorBrushCache.Get(theme.TableHeaderBackground) };
        for (var i = 0; i < columns; i++)
        {
            var text = i < table.Headers.Count ? table.Headers[i] : "";
            var headerPara = new Paragraph
            {
                FontWeight = FontWeights.Bold,
                TextAlignment = GetAlignment(table, i),
                Padding = new Thickness(13, 6, 13, 6),
                BorderBrush = EditorBrushCache.Get(theme.TableBorderColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
            };
            _inlineRenderer.RenderInlines(headerPara, _markdownParser.ParseInlines(text));
            headerRow.Cells.Add(new TableCell(headerPara));
        }
        rg.Rows.Add(headerRow);

        // Data rows with alternating backgrounds
        var rowIndex = 0;
        foreach (var row in table.Rows)
        {
            var isAltRow = rowIndex % 2 == 1;
            var dataRow = new TableRow
            {
                Background = isAltRow
                    ? EditorBrushCache.Get(theme.TableAltRowBackground)
                    : Brushes.Transparent,
            };
            for (var i = 0; i < columns; i++)
            {
                var text = i < row.Count ? row[i] : "";
                var dataPara = new Paragraph
                {
                    TextAlignment = GetAlignment(table, i),
                    Padding = new Thickness(13, 6, 13, 6),
                    BorderBrush = EditorBrushCache.Get(theme.TableBorderColor),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                };
                _inlineRenderer.RenderInlines(dataPara, _markdownParser.ParseInlines(text));
                dataRow.Cells.Add(new TableCell(dataPara));
            }
            rg.Rows.Add(dataRow);
            rowIndex++;
        }

        tableEl.RowGroups.Add(rg);
        return tableEl;
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
