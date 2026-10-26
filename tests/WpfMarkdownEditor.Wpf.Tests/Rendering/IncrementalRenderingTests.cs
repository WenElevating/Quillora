using System.Diagnostics;
using System.Windows.Documents;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.SyntaxHighlighting;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;
using Xunit.Abstractions;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class IncrementalRenderingTests
{
    private readonly MarkdownParser _parser = new();
    private readonly ITestOutputHelper _output;

    public IncrementalRenderingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RenderIncremental_NoChanges_ReusesEveryBlock()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            const string markdown = "alpha\n\nbeta\n\ngamma";
            var document = Render(renderer, markdown);
            var original = TopBlocks(document);

            var updated = RenderIncremental(renderer, document, markdown);

            Assert.Same(document, updated);
            Assert.Equal(original, TopBlocks(document));
            Assert.Equal(3, renderer.LastRenderStats.Reused);
            Assert.Equal(0, renderer.LastRenderStats.Rebuilt);
            Assert.Equal(0, renderer.LastRenderStats.Removed);
        });
    }

    [Fact]
    public void RenderIncremental_EditMiddleBlock_RebuildsOnlyThatBlock()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "alpha\n\nbeta\n\ngamma");
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, "alpha\n\nBETA\n\ngamma");
            var updated = TopBlocks(document);

            Assert.Equal(3, updated.Count);
            Assert.Same(original[0], updated[0]);
            Assert.NotSame(original[1], updated[1]);
            Assert.Same(original[2], updated[2]);
            Assert.Equal(2, renderer.LastRenderStats.Reused);
            Assert.Equal(1, renderer.LastRenderStats.Rebuilt);
            Assert.Equal(1, renderer.LastRenderStats.Removed);
        });
    }

    [Fact]
    public void RenderIncremental_InsertAtTop_ReusesExistingBlocks()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "alpha\n\nbeta");
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, "intro\n\nalpha\n\nbeta");
            var updated = TopBlocks(document);

            Assert.Equal(3, updated.Count);
            Assert.Same(original[0], updated[1]);
            Assert.Same(original[1], updated[2]);
            Assert.Equal(2, renderer.LastRenderStats.Reused);
            Assert.Equal(1, renderer.LastRenderStats.Rebuilt);
            Assert.Equal(0, renderer.LastRenderStats.Removed);
        });
    }

    [Fact]
    public void RenderIncremental_DeleteMiddleBlock_RemovesOnlyThatBlock()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "alpha\n\nbeta\n\ngamma");
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, "alpha\n\ngamma");
            var updated = TopBlocks(document);

            Assert.Equal(2, updated.Count);
            Assert.Same(original[0], updated[0]);
            Assert.Same(original[2], updated[1]);
            Assert.Equal(2, renderer.LastRenderStats.Reused);
            Assert.Equal(0, renderer.LastRenderStats.Rebuilt);
            Assert.Equal(1, renderer.LastRenderStats.Removed);
        });
    }

    [Fact]
    public void RenderIncremental_ReorderBlocks_ReusesMovedElementsInNewOrder()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "a\n\nb\n\nc");
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, "c\n\na\n\nb");
            var updated = TopBlocks(document);

            Assert.Equal(new[] { original[2], original[0], original[1] }, updated);
            Assert.Equal(3, renderer.LastRenderStats.Reused);
            Assert.Equal(0, renderer.LastRenderStats.Rebuilt);
        });
    }

    [Fact]
    public void RenderIncremental_DuplicateParagraphs_ReuseInSourceOrder()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "same\n\nsame\n\nsame");
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, "same\n\nsame\n\nother");
            var updated = TopBlocks(document);

            Assert.Equal(3, updated.Count);
            Assert.Same(original[0], updated[0]);
            Assert.Same(original[1], updated[1]);
            Assert.NotSame(original[2], updated[2]);
            Assert.Equal(2, renderer.LastRenderStats.Reused);
            Assert.Equal(1, renderer.LastRenderStats.Rebuilt);
        });
    }

    [Fact]
    public void RenderIncremental_DocumentWithoutMetadata_FallsBackToFullRender()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            const string markdown = "alpha\n\nbeta";
            var document = renderer.Render(_parser.Parse(markdown));
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, markdown);
            var updated = TopBlocks(document);

            Assert.Equal(2, updated.Count);
            Assert.NotSame(original[0], updated[0]);
            Assert.NotSame(original[1], updated[1]);
            Assert.Equal(0, renderer.LastRenderStats.Reused);
            Assert.Equal(2, renderer.LastRenderStats.Rebuilt);
        });
    }

    [Fact]
    public void RenderIncremental_InsertAbove_UpdatesSourceLineMetadata()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "alpha\n\nbeta");
            var original = TopBlocks(document);
            Assert.True(RenderedBlockMetadata.TryGet(original[1], out var before));
            Assert.Equal(3, before.SourceStartLine);

            RenderIncremental(renderer, document, "intro\n\nalpha\n\nbeta");
            var updated = TopBlocks(document);
            Assert.True(RenderedBlockMetadata.TryGet(updated[2], out var after));
            Assert.Equal(5, after.SourceStartLine);
        });
    }

    [Fact]
    public void RenderIncremental_CrLfDocument_ReusesUnchangedBlocks()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var document = Render(renderer, "alpha\r\n\r\nbeta");
            var original = TopBlocks(document);

            RenderIncremental(renderer, document, "alpha\r\n\r\nBETA");

            Assert.Equal(1, renderer.LastRenderStats.Reused);
            Assert.Equal(1, renderer.LastRenderStats.Rebuilt);
            Assert.Same(original[0], TopBlocks(document)[0]);
        });
    }

    [Fact]
    public void RenderIncremental_LargeDocument_SingleEditRebuildsAtMostThreeBlocks()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = CreateRenderer();
            var lines = Enumerable.Range(0, 1000).Select(i => $"paragraph {i}").ToArray();
            var markdown = string.Join("\n\n", lines);
            var fullRender = Stopwatch.StartNew();
            var document = Render(renderer, markdown);
            fullRender.Stop();
            var original = TopBlocks(document);

            lines[500] = "changed 500";
            RenderIncremental(renderer, document, string.Join("\n\n", lines));
            var updated = TopBlocks(document);

            _output.WriteLine(
                $"1000 blocks: full render {fullRender.ElapsedMilliseconds}ms; " +
                $"single edit reused={renderer.LastRenderStats.Reused}, " +
                $"rebuilt={renderer.LastRenderStats.Rebuilt}, " +
                $"removed={renderer.LastRenderStats.Removed}, " +
                $"diff={renderer.LastRenderStats.ElapsedMilliseconds}ms");

            Assert.Equal(1000, updated.Count);
            Assert.NotSame(original[500], updated[500]);
            Assert.Same(original[499], updated[499]);
            Assert.Same(original[501], updated[501]);
            Assert.InRange(renderer.LastRenderStats.Rebuilt, 1, 3);
            Assert.True(renderer.LastRenderStats.Reused >= 997);
            Assert.True(renderer.LastRenderStats.ElapsedMilliseconds >= 0);
        });
    }

    [Fact]
    public void Render_CodeHeavyDocument_ReportsTiming()
    {
        WpfTestHost.Run(() =>
        {
            var codeLines = string.Join(
                "\n",
                Enumerable.Range(0, 40).Select(i => $"var value{i} = {i}; // comment {i}"));
            var markdown = string.Join(
                "\n\n",
                Enumerable.Range(0, 100).Select(_ => "```csharp\n" + codeLines + "\n```"));

            var stopwatch = Stopwatch.StartNew();
            var blocks = _parser.Parse(markdown);
            stopwatch.Stop();
            var parseMs = stopwatch.ElapsedMilliseconds;

            var highlighter = new SyntaxHighlighter();
            stopwatch.Restart();
            var tokenCount = 0;
            foreach (var codeBlock in blocks.OfType<CodeBlock>())
                tokenCount += highlighter.Tokenize(codeBlock.Code, codeBlock.Language).Count;
            stopwatch.Stop();
            var tokenizeMs = stopwatch.ElapsedMilliseconds;

            var renderer = new FlowDocumentRenderer(EditorTheme.Light, null, highlighter);
            stopwatch.Restart();
            var document = renderer.Render(blocks);
            stopwatch.Stop();

            _output.WriteLine(
                $"100 code blocks x 40 lines: parse {parseMs}ms, tokenize {tokenizeMs}ms ({tokenCount} tokens), " +
                $"render {stopwatch.ElapsedMilliseconds}ms, document blocks={document.Blocks.Count}");
            Assert.Equal(100, document.Blocks.Count);
            var codeParagraph = document.Blocks
                .OfType<Section>()
                .SelectMany(static section => section.Blocks.OfType<Paragraph>())
                .First();
            Assert.Single(codeParagraph.Inlines);
        });
    }

    private FlowDocumentRenderer CreateRenderer() => new(EditorTheme.Light);

    private FlowDocument Render(FlowDocumentRenderer renderer, string markdown) =>
        renderer.RenderIncremental(null, _parser.Parse(markdown), markdown);

    private FlowDocument RenderIncremental(FlowDocumentRenderer renderer, FlowDocument document, string markdown) =>
        renderer.RenderIncremental(document, _parser.Parse(markdown), markdown);

    private static List<WpfBlock> TopBlocks(FlowDocument document) =>
        document.Blocks.Cast<WpfBlock>().ToList();
}
