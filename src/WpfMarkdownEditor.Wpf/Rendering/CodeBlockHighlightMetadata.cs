using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Wpf.SyntaxHighlighting;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// A code block that renders as plain text first and can be syntax-highlighted later,
/// once it is (or becomes) visible. WPF creates one text element per colored run, so
/// eagerly highlighting every code block in a large document is prohibitively expensive.
/// </summary>
internal sealed class CodeBlockTarget(
    Paragraph paragraph,
    string code,
    string? language,
    SyntaxHighlighter? highlighter,
    EditorTheme theme)
{
    private const int MaxTokensToHighlight = 1200;
    private const int MaxCodeLengthToHighlight = 20_000;

    public Paragraph Paragraph { get; } = paragraph;

    public bool IsHighlighted { get; private set; }

    public void Highlight()
    {
        if (IsHighlighted)
            return;

        IsHighlighted = true;
        if (highlighter is null
            || string.IsNullOrEmpty(language)
            || code.Length > MaxCodeLengthToHighlight)
        {
            return;
        }

        var tokens = highlighter.Tokenize(code, language);
        if (tokens.Count > MaxTokensToHighlight)
            return;

        ApplyTokens(tokens);
    }

    private void ApplyTokens(List<SyntaxToken> tokens)
    {
        var defaultBrush = Frozen(theme.CodeForeground);
        var brushes = new Dictionary<TokenType, SolidColorBrush>
        {
            [TokenType.Plain] = defaultBrush,
            [TokenType.Whitespace] = defaultBrush,
            [TokenType.Identifier] = defaultBrush,
            [TokenType.Punctuation] = defaultBrush,
            [TokenType.Keyword] = Frozen(theme.SyntaxKeywordColor),
            [TokenType.Comment] = Frozen(theme.SyntaxCommentColor),
            [TokenType.String] = Frozen(theme.SyntaxStringColor),
            [TokenType.Number] = Frozen(theme.SyntaxNumberColor),
            [TokenType.Type] = Frozen(theme.SyntaxTypeColor),
        };

        var segments = new List<(SolidColorBrush Brush, string Text)>();
        var buffer = new StringBuilder();
        SolidColorBrush? currentBrush = null;

        foreach (var token in tokens)
        {
            var brush = brushes.TryGetValue(token.Type, out var mapped) ? mapped : defaultBrush;
            if (currentBrush is not null && !ReferenceEquals(currentBrush, brush))
            {
                if (buffer.Length > 0)
                    segments.Add((currentBrush, buffer.ToString()));

                buffer.Clear();
            }

            currentBrush = brush;
            buffer.Append(token.Text);
        }

        if (currentBrush is not null && buffer.Length > 0)
            segments.Add((currentBrush, buffer.ToString()));

        Paragraph.Inlines.Clear();
        foreach (var (brush, text) in segments)
            Paragraph.Inlines.Add(new Run(text) { Foreground = brush });
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

internal static class CodeBlockHighlightMetadata
{
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target",
        typeof(CodeBlockTarget),
        typeof(CodeBlockHighlightMetadata),
        new PropertyMetadata(null));

    public static void Set(Paragraph paragraph, CodeBlockTarget target) =>
        paragraph.SetValue(TargetProperty, target);

    public static CodeBlockTarget? Get(Paragraph paragraph) =>
        paragraph.GetValue(TargetProperty) as CodeBlockTarget;
}
