using WpfMarkdownEditor.Core.Parsing;

namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// Parsed preview content plus the precomputed source mapping needed for incremental rendering.
/// Created on a background thread and consumed by <see cref="FlowDocumentRenderer"/> on the UI thread.
/// </summary>
internal sealed record PreviewRenderInput(
    string Markdown,
    List<Block> Blocks,
    SourceLineIndex LineIndex,
    BlockSignature[] Signatures);
