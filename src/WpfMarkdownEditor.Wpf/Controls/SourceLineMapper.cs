using System.Windows.Controls;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Maps 1-based source lines to TextBox offsets and back. The TextBox line APIs count
/// soft-wrapped display lines, while block metadata, the outline and the preview map to
/// physical source lines; every conversion between the two must go through this mapper.
/// </summary>
internal static class SourceLineMapper
{
    /// <summary>
    /// Number of source lines: newline count plus one, ignoring soft wraps.
    /// </summary>
    public static int GetSourceLineCount(TextBox textBox)
    {
        var text = textBox.Text;
        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }

    /// <summary>
    /// 1-based source line that contains <paramref name="characterIndex"/>, counting
    /// newlines instead of display rows.
    /// </summary>
    public static int GetSourceLine(TextBox textBox, int characterIndex)
    {
        var text = textBox.Text;
        if (characterIndex <= 0)
            return 1;

        var line = 1;
        var limit = Math.Min(characterIndex, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    /// <summary>
    /// 1-based source line currently at the top of the viewport. Uses the display-line
    /// index only to find the character offset of the first visible row, then converts
    /// that offset back to a source line.
    /// </summary>
    public static int GetFirstVisibleSourceLine(TextBox textBox)
    {
        var displayLine = textBox.GetFirstVisibleLineIndex();
        if (displayLine < 0)
            return 1;

        var offset = textBox.GetCharacterIndexFromLineIndex(displayLine);
        return offset < 0 ? 1 : GetSourceLine(textBox, offset);
    }

    /// <summary>
    /// Character offset where the 1-based source line starts. Clamps to the text end
    /// when the document has fewer lines than requested.
    /// </summary>
    public static int GetOffsetOfSourceLine(TextBox textBox, int line)
    {
        if (line <= 1)
            return 0;

        var text = textBox.Text;
        var remaining = line - 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;

            remaining--;
            if (remaining == 0)
                return Math.Min(i + 1, text.Length);
        }

        return text.Length;
    }
}
