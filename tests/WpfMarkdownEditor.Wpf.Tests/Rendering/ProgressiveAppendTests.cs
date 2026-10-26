using System.Windows.Documents;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;
using CoreBlock = WpfMarkdownEditor.Core.Parsing.Block;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class ProgressiveAppendTests
{
    private readonly MarkdownParser _parser = new();

    [Fact]
    public void AppendBlockRange_FirstChunk_AppendsBlocksWithMetadata()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = new FlowDocumentRenderer(EditorTheme.Light);
            var (blocks, signatures) = Parse("one\n\ntwo\n\nthree\n\nfour");
            var document = renderer.CreateProgressiveDocument();

            var result = renderer.AppendBlockRange(document, blocks, signatures, 0, 2);
            var appended = document.Blocks.Cast<System.Windows.Documents.Block>().ToList();

            Assert.Equal(2, result.NextIndex);
            Assert.Equal(2, result.RenderedCount);
            Assert.Equal(2, result.AppendedBlocks.Count);
            Assert.Equal(2, appended.Count);
            Assert.True(RenderedBlockMetadata.TryGet(appended[0], out var first));
            Assert.Equal(1, first.SourceStartLine);
            Assert.True(RenderedBlockMetadata.TryGet(appended[1], out var second));
            Assert.Equal(3, second.SourceStartLine);
        });
    }

    [Fact]
    public void AppendBlockRange_SecondChunk_AppendsInOrderAndClampsCount()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = new FlowDocumentRenderer(EditorTheme.Light);
            var (blocks, signatures) = Parse("one\n\ntwo\n\nthree\n\nfour");
            var document = renderer.CreateProgressiveDocument();

            var first = renderer.AppendBlockRange(document, blocks, signatures, 0, 2);
            var second = renderer.AppendBlockRange(document, blocks, signatures, first.NextIndex, 99);

            Assert.Equal(4, second.NextIndex);
            Assert.Equal(2, second.RenderedCount);
            Assert.Equal(4, document.Blocks.Count);
            Assert.Equal(4, document.Blocks.Cast<System.Windows.Documents.Block>().Count());
        });
    }

    [Fact]
    public void AppendBlockRange_EmptyRange_ReturnsCurrentIndex()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = new FlowDocumentRenderer(EditorTheme.Light);
            var (blocks, signatures) = Parse("one\n\ntwo");
            var document = renderer.CreateProgressiveDocument();

            var result = renderer.AppendBlockRange(document, blocks, signatures, 0, 0);

            Assert.Equal(0, result.NextIndex);
            Assert.Empty(result.AppendedBlocks);
            Assert.Empty(document.Blocks);
        });
    }

    private (List<CoreBlock> Blocks, BlockSignature[] Signatures) Parse(string markdown)
    {
        var blocks = _parser.Parse(markdown);
        var index = SourceLineIndex.Create(markdown);
        return (blocks, BlockSignatureFactory.Create(blocks, index));
    }
}
