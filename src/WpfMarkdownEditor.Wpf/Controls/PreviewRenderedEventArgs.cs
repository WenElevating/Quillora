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

    /// <summary>
    /// False when this event describes the first chunk of a progressive render and more
    /// blocks will be appended. Consumers that need the complete document should wait
    /// for the event with <c>IsComplete = true</c>.
    /// </summary>
    public bool IsComplete { get; init; } = true;
}
