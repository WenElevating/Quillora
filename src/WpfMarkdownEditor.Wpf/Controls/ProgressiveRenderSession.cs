using System.Windows.Documents;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Rendering;
using CoreBlock = WpfMarkdownEditor.Core.Parsing.Block;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// State for a progressive first-paint render: the document is mounted after the first
/// chunk and the remaining AST blocks are appended on background dispatcher turns.
/// </summary>
internal sealed class ProgressiveRenderSession
{
    public ProgressiveRenderSession(
        int version,
        string markdown,
        List<CoreBlock> blocks,
        SourceLineIndex lineIndex,
        BlockSignature[] signatures,
        FlowDocument document,
        int nextIndex,
        int renderedBlockCount,
        int firstPaintElapsedMs)
    {
        Version = version;
        Markdown = markdown;
        Blocks = blocks;
        LineIndex = lineIndex;
        Signatures = signatures;
        Document = document;
        NextIndex = nextIndex;
        RenderedBlockCount = renderedBlockCount;
        FirstPaintElapsedMs = firstPaintElapsedMs;
    }

    public int Version { get; set; }

    public string Markdown { get; set; }

    public List<CoreBlock> Blocks { get; set; }

    public SourceLineIndex LineIndex { get; set; }

    public BlockSignature[] Signatures { get; set; }

    public FlowDocument Document { get; }

    public int NextIndex { get; set; }

    public int RenderedBlockCount { get; set; }

    public int FirstPaintElapsedMs { get; set; }

    public bool IsChunkScheduled { get; set; }

    public bool IsComplete => NextIndex >= Blocks.Count;
}
