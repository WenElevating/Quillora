using System.Text;
using System.Text.RegularExpressions;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Pure text operations behind smart Enter/Tab/Backspace handling in the source pane.
/// Handles list, task-list and blockquote prefixes; fenced code is left to the TextBox.
/// </summary>
internal static class MarkdownEditingOperations
{
    private const int IndentSize = 2;
    private const int MaxQuoteDepthWithoutSpace = 32;

    /// <summary>
    /// Continues a list item, task item or blockquote, or exits an empty block.
    /// Returns false for plain lines and for carets inside the marker prefix so the
    /// TextBox keeps its default behavior.
    /// </summary>
    public static bool TryHandleEnter(string text, int caretIndex, out TextEditOperation operation)
    {
        operation = default;

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var lineStart = FindLineStart(text, caretIndex);
        var lineEnd = FindLineEnd(text, caretIndex);
        var lineText = text[lineStart..lineEnd].TrimEnd('\r');

        if (IsInsideFencedCode(text, lineStart))
            return false;

        var line = ParseBlockLine(lineText);
        if (!line.HasList && !line.HasQuote)
            return false;

        // Only continue when the caret is after the marker prefix.
        if (caretIndex < lineStart + line.PrefixEnd)
            return false;

        if (IsEmptyContent(lineText, line))
        {
            // Empty item/quote — clear the markers, keep indentation, insert a plain newline.
            var removeStart = lineStart + line.IndentLength;
            var removeLength = line.PrefixEnd - line.IndentLength;
            var newline = Environment.NewLine;
            var updated = text.Remove(removeStart, removeLength).Insert(removeStart, newline);
            operation = new TextEditOperation(updated, removeStart + newline.Length, 0);
            return true;
        }

        var insertion = Environment.NewLine + BuildContinuationPrefix(lineText, line);
        var inserted = text.Insert(caretIndex, insertion);
        operation = new TextEditOperation(inserted, caretIndex + insertion.Length, 0);
        return true;
    }

    /// <summary>
    /// Indents or outdents the selected list/quote lines. Returns false for plain text
    /// and for any selection that touches a fenced code block.
    /// </summary>
    public static bool TryHandleTab(
        string text,
        int selectionStart,
        int selectionLength,
        bool shift,
        out TextEditOperation operation)
    {
        operation = default;

        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        var lines = SplitLines(text);
        var startLine = GetLineIndex(lines, start);
        var endLine = length > 0 ? GetLineIndex(lines, start + length - 1) : startLine;

        for (var i = startLine; i <= endLine; i++)
        {
            if (IsInsideFencedCode(lines, i))
                return false;
        }

        var anyBlockLine = false;
        for (var i = startLine; i <= endLine; i++)
        {
            var lineText = GetLineText(lines[i]);
            var line = ParseBlockLine(lineText);
            if (line.HasList || line.HasQuote)
            {
                anyBlockLine = true;
                break;
            }
        }

        if (!anyBlockLine)
            return false;

        var edits = new List<LineEdit>();
        for (var i = startLine; i <= endLine; i++)
        {
            var slice = lines[i];
            var lineText = GetLineText(slice);
            if (lineText.Length == 0)
                continue;

            if (shift)
            {
                var leadingSpaces = 0;
                while (leadingSpaces < lineText.Length && leadingSpaces < IndentSize && lineText[leadingSpaces] == ' ')
                    leadingSpaces++;

                if (leadingSpaces > 0)
                    edits.Add(new LineEdit(slice.Start, leadingSpaces, 0));
            }
            else
            {
                edits.Add(new LineEdit(slice.Start, 0, IndentSize));
            }
        }

        if (edits.Count == 0)
        {
            // The key is still handled so Tab does not insert a literal tab in a block line.
            operation = new TextEditOperation(text, start, length);
            return true;
        }

        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(static edit => edit.Position))
        {
            if (edit.RemoveLength > 0)
                builder.Remove(edit.Position, edit.RemoveLength);
            if (edit.InsertLength > 0)
                builder.Insert(edit.Position, new string(' ', edit.InsertLength));
        }

        var newStart = MapPosition(edits, start);
        var newEnd = MapPosition(edits, start + length);
        operation = new TextEditOperation(builder.ToString(), newStart, Math.Max(0, newEnd - newStart));
        return true;
    }

    /// <summary>
    /// Removes a list/task/quote marker when Backspace is pressed inside the marker prefix.
    /// </summary>
    public static bool TryHandleBackspace(
        string text,
        int selectionStart,
        int selectionLength,
        out TextEditOperation operation)
    {
        operation = default;

        if (selectionLength > 0)
            return false;

        var caretIndex = Math.Clamp(selectionStart, 0, text.Length);
        var lineStart = FindLineStart(text, caretIndex);
        var lineEnd = FindLineEnd(text, caretIndex);
        var lineText = text[lineStart..lineEnd].TrimEnd('\r');

        if (IsInsideFencedCode(text, lineStart))
            return false;

        var line = ParseBlockLine(lineText);
        if (!line.HasList && !line.HasQuote)
            return false;

        if (caretIndex > lineStart + line.PrefixEnd)
            return false;

        var removeStart = lineStart + line.IndentLength;
        var removeLength = line.PrefixEnd - line.IndentLength;
        if (removeLength <= 0)
            return false;

        var updated = text.Remove(removeStart, removeLength);
        operation = new TextEditOperation(updated, removeStart, 0);
        return true;
    }

    // Kept for callers/tests that operate on a caret only.
    public static bool TryHandleTab(string text, int caretIndex, bool shift, out TextEditOperation operation) =>
        TryHandleTab(text, caretIndex, 0, shift, out operation);

    private static BlockLine ParseBlockLine(string lineText)
    {
        var indentLength = 0;
        while (indentLength < lineText.Length && lineText[indentLength] == ' ')
            indentLength++;

        var index = indentLength;
        var hasQuote = false;
        var quoteCount = 0;
        while (index < lineText.Length && lineText[index] == '>' && quoteCount < MaxQuoteDepthWithoutSpace)
        {
            hasQuote = true;
            quoteCount++;
            index++;
            if (index < lineText.Length && lineText[index] == ' ')
                index++;
        }

        var afterQuote = index;
        var markerSearch = index;
        var skippedSpaces = 0;
        while (markerSearch < lineText.Length && lineText[markerSearch] == ' ' && skippedSpaces < 3)
        {
            markerSearch++;
            skippedSpaces++;
        }

        if (!TryReadListMarker(lineText, markerSearch, out var marker, out var markerPrefixEnd))
        {
            return new BlockLine(
                indentLength,
                afterQuote,
                hasQuote,
                HasList: false,
                ListMarker: string.Empty,
                IsTask: false,
                MarkerStart: -1,
                MarkerEnd: -1,
                TaskStart: -1,
                TaskEnd: -1);
        }

        var markerEnd = markerSearch + marker.Length;
        index = markerPrefixEnd;
        var taskStart = -1;
        var taskEnd = -1;
        var isTask = false;
        if (TryReadTaskMarker(lineText, index, out var taskPrefixEnd))
        {
            isTask = true;
            taskStart = index;
            taskEnd = index + 3;
            index = taskPrefixEnd;
            while (index < lineText.Length && lineText[index] == ' ')
                index++;
        }

        return new BlockLine(
            indentLength,
            index,
            hasQuote,
            HasList: true,
            marker,
            isTask,
            markerSearch,
            markerEnd,
            taskStart,
            taskEnd);
    }

    private static bool TryReadListMarker(
        string lineText,
        int start,
        out string marker,
        out int end)
    {
        marker = string.Empty;
        end = start;

        if (start >= lineText.Length)
            return false;

        var first = lineText[start];
        if (first is '-' or '*' or '+')
        {
            end = start + 1;
            if (!TryConsumeMarkerSpace(lineText, ref end))
                return false;

            marker = first.ToString();
            return true;
        }

        if (!char.IsAsciiDigit(first))
            return false;

        var markerEnd = start;
        while (markerEnd < lineText.Length && char.IsAsciiDigit(lineText[markerEnd]))
            markerEnd++;

        if (markerEnd >= lineText.Length || lineText[markerEnd] is not ('.' or ')'))
            return false;

        markerEnd++;
        end = markerEnd;
        if (!TryConsumeMarkerSpace(lineText, ref end))
            return false;

        marker = lineText[start..markerEnd];
        return true;
    }

    private static bool TryConsumeMarkerSpace(string lineText, ref int end)
    {
        if (end >= lineText.Length || !char.IsWhiteSpace(lineText[end]))
            return false;

        while (end < lineText.Length && lineText[end] == ' ')
            end++;

        return true;
    }

    private static bool TryReadTaskMarker(string lineText, int start, out int end)
    {
        end = start;
        if (start + 2 >= lineText.Length)
            return false;

        if (lineText[start] != '[' || lineText[start + 2] != ']')
            return false;

        var state = lineText[start + 1];
        if (state is not (' ' or 'x' or 'X'))
            return false;

        end = start + 3;
        if (end < lineText.Length && lineText[end] == ' ')
        {
            while (end < lineText.Length && lineText[end] == ' ')
                end++;
            return true;
        }

        return end == lineText.Length;
    }

    private static string BuildContinuationPrefix(string lineText, BlockLine line)
    {
        var prefix = lineText[..line.PrefixEnd];

        if (line.IsTask)
            prefix = prefix[..line.TaskStart] + "[ ]" + prefix[line.TaskEnd..];

        if (line.HasList)
        {
            var nextMarker = GetNextMarker(line.ListMarker);
            prefix = prefix[..line.MarkerStart] + nextMarker + prefix[line.MarkerEnd..];
        }

        if (line.HasQuote && !prefix.EndsWith(' '))
            prefix += " ";

        return prefix;
    }

    private static string GetNextMarker(string currentMarker)
    {
        if (currentMarker.Length > 1
            && int.TryParse(currentMarker[..^1], out var number))
        {
            var delimiter = currentMarker[^1];
            return $"{number + 1}{delimiter}";
        }

        return currentMarker;
    }

    private static bool IsEmptyContent(string lineText, BlockLine line) =>
        lineText[line.PrefixEnd..].Trim().Length == 0;

    private static int MapPosition(IReadOnlyList<LineEdit> edits, int position)
    {
        var delta = 0;
        foreach (var edit in edits)
        {
            if (edit.RemoveLength > 0)
            {
                if (position <= edit.Position)
                    continue;

                delta += position >= edit.Position + edit.RemoveLength
                    ? -edit.RemoveLength
                    : edit.Position - position;
            }
            else if (edit.InsertLength > 0 && position >= edit.Position)
            {
                delta += edit.InsertLength;
            }
        }

        return Math.Max(0, position + delta);
    }

    private static int FindLineStart(string text, int position)
    {
        var index = Math.Clamp(position, 0, text.Length);
        while (index > 0 && text[index - 1] is not ('\n' or '\r'))
            index--;

        return index;
    }

    private static int FindLineEnd(string text, int position)
    {
        var index = Math.Clamp(position, 0, text.Length);
        while (index < text.Length && text[index] is not ('\n' or '\r'))
            index++;

        return index;
    }

    private static bool IsInsideFencedCode(string text, int lineStart)
    {
        var inFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;
        var index = 0;

        while (index <= text.Length)
        {
            var lineEnd = index;
            while (lineEnd < text.Length && text[lineEnd] is not ('\r' or '\n'))
                lineEnd++;

            var contentStart = index;
            while (contentStart < lineEnd && text[contentStart] == ' ')
                contentStart++;

            var content = text.AsSpan(contentStart, lineEnd - contentStart);

            if (index == lineStart)
                return inFence || TryGetFenceStart(content, out _, out _);

            if (!inFence)
            {
                if (TryGetFenceStart(content, out var startChar, out var startLength))
                {
                    inFence = true;
                    fenceChar = startChar;
                    fenceLength = startLength;
                }
            }
            else if (IsFenceClose(content, fenceChar, fenceLength))
            {
                inFence = false;
            }

            if (lineEnd >= text.Length)
                break;

            index = lineEnd + 1;
            if (text[lineEnd] == '\r' && index < text.Length && text[index] == '\n')
                index++;
        }

        return inFence;
    }

    private static bool IsInsideFencedCode(IReadOnlyList<LineSlice> lines, int lineIndex)
    {
        var inFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        for (var i = 0; i <= lineIndex; i++)
        {
            var content = GetLineText(lines[i]).AsSpan().TrimStart();
            if (i == lineIndex)
                return inFence || TryGetFenceStart(content, out _, out _);

            if (!inFence)
            {
                if (TryGetFenceStart(content, out var startChar, out var startLength))
                {
                    inFence = true;
                    fenceChar = startChar;
                    fenceLength = startLength;
                }
            }
            else if (IsFenceClose(content, fenceChar, fenceLength))
            {
                inFence = false;
            }
        }

        return inFence;
    }

    private static bool TryGetFenceStart(ReadOnlySpan<char> content, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        if (content.Length < 3 || content[0] is not ('`' or '~'))
            return false;

        fenceChar = content[0];
        while (fenceLength < content.Length && content[fenceLength] == fenceChar)
            fenceLength++;

        return fenceLength >= 3;
    }

    private static bool IsFenceClose(ReadOnlySpan<char> content, char fenceChar, int openingLength)
    {
        if (content.Length < openingLength || content[0] != fenceChar)
            return false;

        var length = 0;
        while (length < content.Length && content[length] == fenceChar)
            length++;

        if (length < openingLength)
            return false;

        return content[length..].Trim().Length == 0;
    }

    private static List<LineSlice> SplitLines(string text)
    {
        var lines = new List<LineSlice>();
        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                index++;
                continue;
            }

            lines.Add(new LineSlice(start, index, text[start..index]));
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;

            index++;
            start = index;
        }

        lines.Add(new LineSlice(start, text.Length, text[start..]));
        return lines;
    }

    private static int GetLineIndex(IReadOnlyList<LineSlice> lines, int position)
    {
        var clamped = Math.Clamp(position, 0, int.MaxValue);
        for (var i = 0; i < lines.Count; i++)
        {
            if (clamped < lines[i].End)
                return i;
        }

        return Math.Max(0, lines.Count - 1);
    }

    private static string GetLineText(LineSlice slice) => slice.Text;

    private readonly record struct BlockLine(
        int IndentLength,
        int PrefixEnd,
        bool HasQuote,
        bool HasList,
        string ListMarker,
        bool IsTask,
        int MarkerStart,
        int MarkerEnd,
        int TaskStart,
        int TaskEnd);

    private readonly record struct LineSlice(int Start, int End, string Text);

    private readonly record struct LineEdit(int Position, int RemoveLength, int InsertLength);
}
