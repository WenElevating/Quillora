using WpfMarkdownEditor.Core.Parsing;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Parsed content of the most recent preview render. Consumers (for example the outline
/// pane) can reuse the AST instead of re-parsing the Markdown on every keystroke.
/// </summary>
public sealed class PreviewRenderedEventArgs : EventArgs
{
    public string Markdown { get; init; } = string.Empty;

    public IReadOnlyList<Block> Blocks { get; init; } = [];
}
