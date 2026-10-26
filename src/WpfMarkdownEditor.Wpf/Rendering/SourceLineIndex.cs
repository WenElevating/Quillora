namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// Maps 1-based source line numbers to character offsets and computes stable content
/// hashes without splitting or copying the whole markdown string.
/// </summary>
internal sealed class SourceLineIndex
{
    private readonly string _markdown;
    private readonly int[] _lineStarts;
    private readonly int[] _lineContentEnds;

    private SourceLineIndex(string markdown, int[] lineStarts, int[] lineContentEnds)
    {
        _markdown = markdown;
        _lineStarts = lineStarts;
        _lineContentEnds = lineContentEnds;
    }

    public int LineCount => _lineStarts.Length;

    public static SourceLineIndex Create(string markdown)
    {
        markdown ??= string.Empty;

        var starts = new List<int>();
        var ends = new List<int>();
        var lineStart = 0;
        var index = 0;

        while (index < markdown.Length)
        {
            var character = markdown[index];
            if (character is not ('\r' or '\n'))
            {
                index++;
                continue;
            }

            starts.Add(lineStart);
            ends.Add(index);

            if (character == '\r' && index + 1 < markdown.Length && markdown[index + 1] == '\n')
                index++;

            index++;
            lineStart = index;
        }

        if (lineStart < markdown.Length || starts.Count == 0)
        {
            starts.Add(lineStart);
            ends.Add(markdown.Length);
        }

        return new SourceLineIndex(markdown, [.. starts], [.. ends]);
    }

    /// <summary>
    /// Computes a 64-bit FNV-1a hash over the requested lines (1-based, inclusive).
    /// Line breaks are normalized to '\n' so CRLF and LF documents hash the same.
    /// </summary>
    public bool TryComputeHash(int lineStart, int lineEnd, out ulong hash)
    {
        hash = 0;
        if (lineStart < 1 || lineEnd < lineStart || lineEnd > _lineStarts.Length)
            return false;

        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var value = offsetBasis;
        for (var line = lineStart; line <= lineEnd; line++)
        {
            var index = line - 1;
            var start = _lineStarts[index];
            var end = _lineContentEnds[index];
            for (var i = start; i < end; i++)
            {
                value ^= _markdown[i];
                value *= prime;
            }

            if (line < lineEnd)
            {
                value ^= '\n';
                value *= prime;
            }
        }

        hash = value;
        return true;
    }
}
