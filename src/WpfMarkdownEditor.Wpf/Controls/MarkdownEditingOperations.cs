using System.Text.RegularExpressions;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Pure text operations behind smart Enter/Tab handling in the source pane.
/// </summary>
internal static class MarkdownEditingOperations
{
    private static readonly Regex ListMarkerRegex = new(@"^(\s*)([-*+]|\d+\.)\s", RegexOptions.Compiled);
    private const int IndentSize = 2;

    /// <summary>
    /// Continues a list item, or exits an empty item. Returns false for non-list lines
    /// and for carets before the list marker so the TextBox keeps its default behavior.
    /// </summary>
    public static bool TryHandleEnter(string text, int caretIndex, out TextEditOperation operation)
    {
        operation = default;

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretIndex - 1)) + 1;
        var lineEnd = text.IndexOf('\n', caretIndex);
        if (lineEnd < 0) lineEnd = text.Length;
        var lineText = text[lineStart..lineEnd].TrimEnd('\r');

        var match = ListMarkerRegex.Match(lineText);
        if (!match.Success)
            return false;

        var indent = match.Groups[1].Value;
        var marker = match.Groups[2].Value;
        var markerEnd = match.Index + match.Length;

        // Only auto-continue if the caret is after the marker.
        if (caretIndex < lineStart + markerEnd)
            return false;

        var contentAfterMarker = lineText[markerEnd..];
        if (string.IsNullOrWhiteSpace(contentAfterMarker))
        {
            // Empty list item — clear the marker and insert a plain newline.
            var removeStart = lineStart + indent.Length;
            var removeLength = lineText.Length - indent.Length;
            var newline = Environment.NewLine;
            var updated = text.Remove(removeStart, removeLength).Insert(removeStart, newline);
            operation = new TextEditOperation(updated, removeStart + newline.Length, 0);
        }
        else
        {
            // Non-empty — insert newline with the next list marker at the caret.
            var nextMarker = GetNextMarker(marker);
            var insertion = Environment.NewLine + indent + nextMarker + " ";
            var updated = text.Insert(caretIndex, insertion);
            operation = new TextEditOperation(updated, caretIndex + insertion.Length, 0);
        }

        return true;
    }

    /// <summary>
    /// Indents or outdents a list line. Returns false for non-list lines so the TextBox
    /// handles Tab normally.
    /// </summary>
    public static bool TryHandleTab(string text, int caretIndex, bool shift, out TextEditOperation operation)
    {
        operation = default;

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretIndex - 1)) + 1;
        var lineEnd = text.IndexOf('\n', caretIndex);
        if (lineEnd < 0) lineEnd = text.Length;
        var lineText = text[lineStart..lineEnd].TrimEnd('\r');

        if (!ListMarkerRegex.IsMatch(lineText))
            return false;

        if (shift)
        {
            var leadingSpaces = lineText.Length - lineText.TrimStart(' ').Length;
            var spacesToRemove = Math.Min(IndentSize, leadingSpaces);
            if (spacesToRemove > 0)
            {
                var updated = text.Remove(lineStart, spacesToRemove);
                var caret = caretIndex > lineStart + spacesToRemove
                    ? caretIndex - spacesToRemove
                    : lineStart;
                operation = new TextEditOperation(updated, caret, 0);
            }
            else
            {
                // Nothing to outdent, but the key must stay handled so Tab does not
                // insert a literal tab inside the list line.
                operation = new TextEditOperation(text, caretIndex, 0);
            }
        }
        else
        {
            var updated = text.Insert(lineStart, new string(' ', IndentSize));
            operation = new TextEditOperation(updated, caretIndex + IndentSize, 0);
        }

        return true;
    }

    private static string GetNextMarker(string currentMarker)
    {
        if (int.TryParse(currentMarker[..^1], out var number))
            return $"{number + 1}.";

        return currentMarker; // Unordered: keep the same marker (-, *, +)
    }
}
