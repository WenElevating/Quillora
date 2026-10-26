using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Blocks;

namespace WpfMarkdownEditor.Sample.Services;

public sealed record OutlineEntry(int Level, string Text, int Line);

/// <summary>
/// Builds outline entries from the parsed preview AST instead of re-scanning the
/// Markdown with a regular expression on every keystroke.
/// </summary>
public static class OutlineBuilder
{
    public static List<OutlineEntry> Build(string markdown, IReadOnlyList<Block> blocks)
    {
        var lines = SplitLines(markdown);
        var entries = new List<OutlineEntry>();

        foreach (var heading in blocks.OfType<HeadingBlock>())
        {
            var index = heading.LineStart - 1;
            var lineText = index >= 0 && index < lines.Count ? lines[index] : string.Empty;
            var text = ExtractHeadingText(lineText);
            entries.Add(new OutlineEntry(
                heading.Level,
                string.IsNullOrWhiteSpace(text) ? "…" : text,
                heading.LineStart));
        }

        return entries;
    }

    private static string ExtractHeadingText(string line)
    {
        var text = line.TrimStart();
        var level = 0;
        while (level < text.Length && level < 6 && text[level] == '#')
            level++;

        text = text[level..].Trim();

        var end = text.Length - 1;
        while (end >= 0 && text[end] == '#')
            end--;
        if (end >= 0 && text[end] == ' ')
            end--;

        return text[..(end + 1)].Trim();
    }

    private static List<string> SplitLines(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return [];

        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        return [.. normalized.Split('\n')];
    }
}
