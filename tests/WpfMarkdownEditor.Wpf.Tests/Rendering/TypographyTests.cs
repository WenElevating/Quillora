using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;
using CoreBlock = WpfMarkdownEditor.Core.Parsing.Block;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class TypographyTests
{
    private readonly MarkdownParser _parser = new();
    private readonly FlowDocumentRenderer _renderer = new(EditorTheme.Light);

    [Fact]
    public void DefaultTheme_UsesTyporaBaseline()
    {
        WpfTestHost.Run(() =>
        {
            var theme = EditorTheme.Light;

            Assert.Equal(16, theme.BaseFontSize);
            Assert.Equal(1.6, theme.LineHeightMultiplier);
            Assert.Equal(860, theme.ContentMaxWidth);
            Assert.Equal(30, theme.PagePadding.Left);
            Assert.Equal(100, theme.PagePadding.Bottom);
            Assert.Contains("Open Sans", EditorFonts.Body.Source);
            Assert.Contains("Cascadia Code", EditorFonts.Code.Source);
        });
    }

    [Fact]
    public void FontResource_IsLoadable()
    {
        WpfTestHost.Run(() =>
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/WpfMarkdownEditor.Wpf;component/Theming/EditorFonts.xaml",
                    UriKind.Absolute),
            };

            var family = Assert.IsType<FontFamily>(dictionary["EditorBodyFont"]);
            Assert.Contains("Open Sans", family.Source);
            Assert.NotEmpty(family.GetTypefaces());
        });
    }

    [Fact]
    public void Document_UsesBodyLineHeightAndBaseSize()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(Array.Empty<CoreBlock>());

            Assert.Equal(16, document.FontSize);
            Assert.Equal(25.6, document.LineHeight, precision: 3);
            Assert.Equal(LineStackingStrategy.BlockLineHeight, document.LineStackingStrategy);
            Assert.Equal(30, document.PagePadding.Left);
            Assert.Contains("Open Sans", document.FontFamily.Source);
        });
    }

    [Fact]
    public void Headings_UseTyporaScaleLineHeightAndMargins()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(_parser.Parse("# H1\n\n## H2\n\n### H3\n\n#### H4\n\n##### H5\n\n###### H6"));
            var headings = document.Blocks
                .Select(UnwrapParagraph)
                .Take(6)
                .ToList();

            var expectedSizes = new[] { 36.0, 28.0, 24.0, 20.0, 16.0, 16.0 };
            var expectedLineHeights = new[] { 43.2, 34.3, 34.32, 26.0, 25.6, 25.6 };

            for (var i = 0; i < headings.Count; i++)
            {
                Assert.Equal(expectedSizes[i], headings[i].FontSize, precision: 2);
                Assert.Equal(expectedLineHeights[i], headings[i].LineHeight, precision: 1);
                Assert.Equal(TypographyScale.HeadingMargin(EditorTheme.Light), headings[i].Margin);
            }

            Assert.Equal(
                EditorBrushCache.Get(EditorTheme.Light.MutedHeadingColor),
                headings[5].Foreground);
        });
    }

    [Fact]
    public void Paragraph_UsesBlockSpacing()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(_parser.Parse("one\n\ntwo"));
            var paragraph = Assert.IsType<Paragraph>(document.Blocks.First());

            Assert.Equal(new Thickness(0, 0, 0, 12.8), paragraph.Margin);
        });
    }

    [Fact]
    public void Blockquote_UsesBorderOnlyAndMutedForeground()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(_parser.Parse("> quote"));
            var section = Assert.IsType<Section>(document.Blocks.First());

            Assert.Null(section.Background);
            Assert.Equal(new Thickness(4, 0, 0, 0), section.BorderThickness);
            Assert.Equal(
                EditorBrushCache.Get(EditorTheme.Light.BlockquoteForeground),
                section.Foreground);
        });
    }

    [Fact]
    public void CodeBlockAndInlineCode_ScaleWithTypographyTokens()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(_parser.Parse("```csharp\nvar x = 1;\n```\n\nuse `code` here"));
            var codeSection = Assert.IsType<Section>(document.Blocks.First());
            var codeParagraph = Assert.IsType<Paragraph>(codeSection.Blocks.First());
            Assert.Equal(14.4, codeParagraph.FontSize, precision: 2);
            Assert.Equal(20.88, codeParagraph.LineHeight, precision: 2);

            var paragraph = Assert.IsType<Paragraph>(document.Blocks.Last());
            var codeSpan = paragraph.Inlines.OfType<Span>().Single(static span => span.Inlines.OfType<Run>().Any());
            Assert.Equal(14.4, codeSpan.FontSize, precision: 2);
        });
    }

    [Fact]
    public void Table_HonorsMarkdownAlignment()
    {
        WpfTestHost.Run(() =>
        {
            var markdown = "| Left | Center | Right |\n| :--- | :---: | ---: |\n| a | b | c |";
            var document = _renderer.Render(_parser.Parse(markdown));
            var table = Assert.IsType<Table>(document.Blocks.First());
            var row = table.RowGroups.First().Rows.First();

            Assert.Equal(TextAlignment.Left, CellParagraph(row, 0).TextAlignment);
            Assert.Equal(TextAlignment.Center, CellParagraph(row, 1).TextAlignment);
            Assert.Equal(TextAlignment.Right, CellParagraph(row, 2).TextAlignment);
        });
    }

    [Fact]
    public void ThematicBreak_UsesFullWidthBorderNotCharacters()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(_parser.Parse("above\n\n---\n\nbelow"));
            var container = Assert.IsType<BlockUIContainer>(
                document.Blocks.Cast<System.Windows.Documents.Block>().ElementAt(1));
            var border = Assert.IsType<Border>(container.Child);
            Assert.Equal(2, border.Height);
            Assert.NotNull(border.Background);
        });
    }

    [Fact]
    public void TaskList_RendersCheckboxesAndHidesBulletMarkers()
    {
        WpfTestHost.Run(() =>
        {
            var document = _renderer.Render(_parser.Parse("- [ ] todo\n- [x] done"));
            var list = Assert.IsType<System.Windows.Documents.List>(document.Blocks.First());
            Assert.Equal(TextMarkerStyle.None, list.MarkerStyle);

            var boxes = list.ListItems
                .SelectMany(static item => item.Blocks.OfType<Paragraph>())
                .SelectMany(static paragraph => paragraph.Inlines.OfType<InlineUIContainer>())
                .Select(static inline => Assert.IsType<CheckBox>(inline.Child))
                .ToList();

            Assert.Equal(2, boxes.Count);
            Assert.False(boxes[0].IsChecked);
            Assert.True(boxes[1].IsChecked);
        });
    }

    [Fact]
    public void PageLayout_AppliesReadableColumnWidths()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = new Window { Content = viewer, Width = 1200, Height = 600 };
            window.Show();
            try
            {
                viewer.Document = _renderer.Render(_parser.Parse("text"));
                window.UpdateLayout();
                using var layout = new PreviewPageLayoutController(viewer, () => EditorTheme.Light);

                layout.Apply();
                var available = viewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
                var contentWidth = available - viewer.Document!.PagePadding.Left - viewer.Document.PagePadding.Right;
                Assert.InRange(contentWidth, 859, 861);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ReadingMode_HidesSourceAndPreviewChrome()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();

            editor.ShowSource = false;
            Assert.Equal(0, editor.SourceColumn.Width.Value);
            Assert.Equal(Visibility.Collapsed, editor.EditorSplitter.Visibility);
            Assert.Equal(Visibility.Collapsed, editor.PreviewToolbarBorder.Visibility);

            editor.ShowSource = true;
            Assert.Equal(1, editor.SourceColumn.Width.Value);
            Assert.Equal(Visibility.Visible, editor.EditorSplitter.Visibility);
            Assert.Equal(Visibility.Visible, editor.PreviewToolbarBorder.Visibility);
        });
    }

    private static Paragraph UnwrapParagraph(System.Windows.Documents.Block block) =>
        block switch
        {
            Paragraph paragraph => paragraph,
            Section section => section.Blocks.OfType<Paragraph>().First(),
            _ => throw new InvalidOperationException($"Unexpected heading block {block.GetType().Name}"),
        };

    private static Paragraph CellParagraph(TableRow row, int index) =>
        Assert.IsType<Paragraph>(row.Cells.ElementAt(index).Blocks.First());
}
