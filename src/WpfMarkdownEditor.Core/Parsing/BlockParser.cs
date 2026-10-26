using System.Text;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Core.Parsing.Html;
using WpfMarkdownEditor.Core.Parsing.Inlines;

namespace WpfMarkdownEditor.Core.Parsing;

/// <summary>
/// Identifies and parses block-level Markdown elements from lines.
/// </summary>
internal sealed class BlockParser
{
    private readonly InlineParser _inlineParser = new();
    private readonly HtmlParsingFacade _htmlParser = new();

    /// <summary>
    /// Parse all blocks from the source text.
    /// </summary>
    public List<Block> ParseBlocks(LineReader reader)
    {
        var blocks = new List<Block>();
        var state = new ParserState();

        while (reader.HasMore)
        {
            var line = reader.PeekLine();
            if (line is null) break;

            // Skip blank lines
            if (string.IsNullOrWhiteSpace(line.Content))
            {
                reader.ReadLine();
                // End current list on blank line if not continuing
                if (state.InList) state.InList = false;
                continue;
            }

            var block = TryParseBlock(reader, state);
            if (block is not null)
                blocks.Add(block);
        }

        return blocks;
    }

    private Block? TryParseBlock(LineReader reader, ParserState state)
    {
        var line = reader.PeekLine()!;
        var content = line.Content;
        var stripped = content.TrimStart();
        var indent = content.Length - stripped.Length;

        // ATX Heading: # H1 through ###### H6
        if (stripped.Length > 0 && stripped[0] == '#' && (stripped.Length < 2 || stripped[1] == ' ' || stripped[1] == '#'))
        {
            return ParseAtxHeading(reader);
        }

        // Thematic break: ---, ***, ___ (with optional spaces)
        if (IsThematicBreak(stripped))
        {
            reader.ReadLine();
            return new ThematicBreakBlock { LineStart = line.LineNumber, LineEnd = line.LineNumber };
        }

        // Fenced code block start: ``` or ~~~
        if (stripped is ['`', ..] or ['~', ..] && IsFenceStart(stripped, out var fenceChar, out var fenceLen, out var lang))
        {
            return ParseFencedCodeBlock(reader, state, line, fenceChar, fenceLen, lang);
        }

        // Blockquote: >
        if (stripped.Length > 0 && stripped[0] == '>')
        {
            return ParseBlockquote(reader, state);
        }

        // Unordered list: -, *, +
        if (stripped.Length > 0 && (stripped[0] == '-' || stripped[0] == '*' || stripped[0] == '+')
            && stripped.Length > 1 && stripped[1] == ' ')
        {
            return ParseUnorderedList(reader, state);
        }

        // Ordered list: 1. or 1)
        if (TryParseOrderedListStart(stripped, out var startNum))
        {
            return ParseOrderedList(reader, state, startNum);
        }

        // Indented code block: 4+ spaces
        if (indent >= 4)
        {
            return ParseIndentedCodeBlock(reader);
        }

        // HTML block subset: only promote when the tag starts the Markdown block.
        if (stripped.Length > 0 && stripped[0] == '<')
        {
            var htmlBlock = TryParseHtmlBlock(reader, line, indent);
            if (htmlBlock is not null)
                return htmlBlock;
        }

        // Table (GFM): look ahead for separator line
        if (stripped.Contains('|'))
        {
            var tableBlock = TryParseTable(reader, line);
            if (tableBlock is not null)
                return tableBlock;
        }

        // Paragraph (fallback) — check for setext heading after
        return ParseParagraph(reader, state);
    }

    #region ATX Heading

    private Block ParseAtxHeading(LineReader reader)
    {
        var line = reader.ReadLine()!;
        var content = line.Content.TrimStart();
        var level = 0;
        while (level < content.Length && level < 6 && content[level] == '#')
            level++;

        // Strip leading # and space
        var text = content[level..].TrimStart();
        // Strip closing # sequence
        var end = text.Length - 1;
        while (end >= 0 && text[end] == '#')
            end--;
        if (end >= 0 && text[end] == ' ')
            end--;
        text = text[..(end + 1)].Trim();

        return new HeadingBlock
        {
            Level = level,
            LineStart = line.LineNumber,
            LineEnd = line.LineNumber,
            Inlines = _inlineParser.ParseInlines(text)
        };
    }

    #endregion

    #region Thematic Break

    private static bool IsThematicBreak(string stripped)
    {
        if (stripped.Length < 3) return false;
        char? breakChar = null;
        var count = 0;
        foreach (var c in stripped)
        {
            if (c == ' ' || c == '\t') continue;
            if (breakChar is null)
            {
                if (c is not ('-' or '*' or '_')) return false;
                breakChar = c;
                count++;
            }
            else if (c == breakChar)
            {
                count++;
            }
            else
            {
                return false;
            }
        }
        return count >= 3;
    }

    #endregion

    #region Fenced Code Block

    private static bool IsFenceStart(string stripped, out char fenceChar, out int fenceLen, out string? lang)
    {
        fenceChar = default;
        fenceLen = 0;
        lang = null;

        if (stripped.Length < 3) return false;
        var ch = stripped[0];
        if (ch != '`' && ch != '~') return false;

        var i = 0;
        while (i < stripped.Length && stripped[i] == ch)
            i++;

        if (i < 3) return false;
        // Backtick fences cannot have backticks in info string
        if (ch == '`' && stripped.IndexOf('`', i) >= 0) return false;

        fenceChar = ch;
        fenceLen = i;
        lang = stripped[i..].Trim();
        if (lang.Length == 0) lang = null;
        return true;
    }

    private Block ParseFencedCodeBlock(LineReader reader, ParserState state, LineInfo startLine, char fenceChar, int fenceLen, string? lang)
    {
        reader.ReadLine(); // consume opening fence

        var code = new StringBuilder();
        var endLine = startLine.LineNumber;

        while (reader.HasMore)
        {
            var line = reader.ReadLine()!;
            var stripped = line.Content.Trim();

            // Closing fence
            if (stripped.Length >= fenceLen && stripped.All(c => c == fenceChar))
            {
                endLine = line.LineNumber;
                break;
            }

            if (code.Length > 0) code.AppendLine();
            code.Append(line.Content);
            endLine = line.LineNumber;
        }

        return new CodeBlock
        {
            Language = lang,
            Code = code.ToString(),
            LineStart = startLine.LineNumber,
            LineEnd = endLine
        };
    }

    #endregion

    #region Indented Code Block

    private Block ParseIndentedCodeBlock(LineReader reader)
    {
        var firstLine = reader.ReadLine()!;
        var code = new StringBuilder(firstLine.Content[4..]);
        var endLine = firstLine.LineNumber;
        var blankLineCount = 0;

        while (reader.HasMore)
        {
            var peeked = reader.PeekLine();
            if (peeked is null) break;

            if (string.IsNullOrWhiteSpace(peeked.Content))
            {
                blankLineCount++;
                // Two or more consecutive blank lines end the indented code block
                if (blankLineCount >= 2) break;
                reader.ReadLine();
                continue;
            }

            if (peeked.IndentLevel < 4)
            {
                // Non-blank, non-indented line ends the block
                break;
            }

            // Add any blank lines that were consumed
            for (var b = 0; b < blankLineCount; b++)
                code.AppendLine();
            blankLineCount = 0;

            code.AppendLine();
            code.Append(peeked.Content[4..]);
            reader.ReadLine();
            endLine = peeked.LineNumber;
        }

        return new CodeBlock
        {
            Code = code.ToString(),
            LineStart = firstLine.LineNumber,
            LineEnd = endLine
        };
    }

    #endregion

    #region Blockquote

    private Block ParseBlockquote(LineReader reader, ParserState state)
    {
        var lines = new List<(string content, int lineNum)>();
        var firstLine = reader.ReadLine()!;
        var startLine = firstLine.LineNumber;
        var endLine = startLine;

        // Strip > prefix and collect lines
        var stripped = StripBlockquoteMarker(firstLine.Content);
        lines.Add((stripped, firstLine.LineNumber));

        while (reader.HasMore)
        {
            var peeked = reader.PeekLine()!;
            if (string.IsNullOrWhiteSpace(peeked.Content)) break;
            var peekStripped = peeked.Content.TrimStart();
            if (peekStripped.Length == 0 || peekStripped[0] != '>') break;

            reader.ReadLine();
            stripped = StripBlockquoteMarker(peeked.Content);
            lines.Add((stripped, peeked.LineNumber));
            endLine = peeked.LineNumber;
        }

        // Parse inner content recursively
        var innerText = string.Join("\n", lines.Select(l => l.content));
        var innerReader = new LineReader(innerText);
        var children = ParseBlocks(innerReader);

        return new BlockquoteBlock
        {
            Children = children,
            LineStart = startLine,
            LineEnd = endLine
        };
    }

    private static string StripBlockquoteMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '>')
        {
            var rest = trimmed[1..];
            return rest.Length > 0 && rest[0] == ' ' ? rest[1..] : rest;
        }
        return line;
    }

    #endregion

    #region Lists

    private Block ParseUnorderedList(LineReader reader, ParserState state) =>
        ParseListCore(reader, state, isOrdered: false, startNum: 1);

    private Block ParseOrderedList(LineReader reader, ParserState state, int startNum) =>
        ParseListCore(reader, state, isOrdered: true, startNum: startNum);

    private ListBlock ParseListCore(LineReader reader, ParserState state, bool isOrdered, int startNum)
    {
        var items = new List<ListItem>();
        var startLine = 0;
        var endLine = 0;
        var baseIndent = -1;

        while (reader.HasMore)
        {
            var peeked = reader.PeekLine();
            if (peeked is null) break;
            if (string.IsNullOrWhiteSpace(peeked.Content)) break;

            var content = peeked.Content;
            var stripped = content.TrimStart();
            var indent = content.Length - stripped.Length;

            // Verify the line starts the expected list type
            if (isOrdered)
            {
                if (!TryParseOrderedListStart(stripped, out _)) break;
            }
            else
            {
                if (stripped.Length < 2 || stripped[0] is not ('-' or '*' or '+') || stripped[1] != ' ') break;
            }

            if (baseIndent < 0) baseIndent = indent;
            if (indent != baseIndent) break;
            if (startLine == 0) startLine = peeked.LineNumber;

            // Extract the item text (after the marker)
            string itemText;
            if (isOrdered)
            {
                var afterNum = 0;
                while (afterNum < stripped.Length && char.IsDigit(stripped[afterNum])) afterNum++;
                if (afterNum < stripped.Length) afterNum++; // skip '.' or ')'
                itemText = afterNum < stripped.Length ? stripped[afterNum..].TrimStart() : "";
            }
            else
            {
                itemText = stripped[2..].TrimStart();
            }

            var isTask = TryStripTaskMarker(ref itemText, out var isTaskChecked);

            reader.ReadLine();
            endLine = peeked.LineNumber;

            var itemBlocks = new List<Block>();
            while (reader.HasMore)
            {
                var cont = reader.PeekLine();
                if (cont is null || string.IsNullOrWhiteSpace(cont.Content)) break;

                var contContent = cont.Content;
                var contStripped = contContent.TrimStart();
                var contIndent = contContent.Length - contStripped.Length;

                if (contIndent <= baseIndent) break;

                // Nested unordered list?
                if (contStripped.Length >= 2 && contStripped[0] is ('-' or '*' or '+') && contStripped[1] == ' ')
                {
                    FlushItemText(ref itemText, itemBlocks, peeked.LineNumber, endLine);
                    var nested = ParseListCore(reader, state, isOrdered: false, startNum: 1);
                    itemBlocks.Add(nested);
                    endLine = nested.LineEnd;
                    continue;
                }

                // Nested ordered list?
                if (TryParseOrderedListStart(contStripped, out var nestedNum))
                {
                    FlushItemText(ref itemText, itemBlocks, peeked.LineNumber, endLine);
                    var nested = ParseListCore(reader, state, isOrdered: true, startNum: nestedNum);
                    itemBlocks.Add(nested);
                    endLine = nested.LineEnd;
                    continue;
                }

                // Continuation text
                itemText += "\n" + cont.Content.Trim();
                reader.ReadLine();
                endLine = cont.LineNumber;
            }

            // Flush remaining item text
            if (itemText.Length > 0)
            {
                itemBlocks.Insert(0, new ParagraphBlock
                {
                    Inlines = _inlineParser.ParseInlines(itemText),
                    LineStart = peeked.LineNumber,
                    LineEnd = endLine
                });
            }

            items.Add(new ListItem
            {
                Blocks = itemBlocks,
                IsTask = isTask,
                IsTaskChecked = isTaskChecked,
            });
        }

        return new ListBlock
        {
            IsOrdered = isOrdered,
            StartNumber = startNum,
            Items = items,
            LineStart = startLine,
            LineEnd = endLine
        };
    }

    private void FlushItemText(ref string itemText, List<Block> itemBlocks, int lineStart, int lineEnd)
    {
        if (itemText.Length == 0) return;
        itemBlocks.Add(new ParagraphBlock
        {
            Inlines = _inlineParser.ParseInlines(itemText),
            LineStart = lineStart,
            LineEnd = lineEnd
        });
        itemText = "";
    }

    private static bool TryStripTaskMarker(ref string itemText, out bool isChecked)
    {
        isChecked = false;
        if (itemText.Length < 3 || itemText[0] != '[' || itemText[2] != ']')
            return false;

        var state = itemText[1];
        if (state is not (' ' or 'x' or 'X'))
            return false;

        if (itemText.Length > 3 && itemText[3] != ' ')
            return false;

        isChecked = state is 'x' or 'X';
        itemText = itemText.Length > 3 ? itemText[4..].TrimStart() : string.Empty;
        return true;
    }

    private static bool TryParseOrderedListStart(string stripped, out int startNum)
    {
        startNum = 0;
        if (stripped.Length < 2) return false;
        var i = 0;
        while (i < stripped.Length && char.IsDigit(stripped[i]))
        {
            startNum = startNum * 10 + (stripped[i] - '0');
            i++;
        }
        if (i == 0 || i >= stripped.Length) return false;
        if (stripped[i] is not ('.' or ')')) return false;
        i++;
        if (i >= stripped.Length || stripped[i] != ' ') return false;
        return true;
    }

    #endregion

    #region Table (GFM)

    private Block? TryParseTable(LineReader reader, LineInfo firstLine)
    {
        // Must have | and next line must be separator (---, :---:, ---:)
        var headerText = firstLine.Content.Trim();
        if (!headerText.Contains('|')) return null;

        // Consume the header line first so we can peek the separator
        reader.ReadLine();

        var separatorPeek = reader.PeekLine();
        if (separatorPeek is null)
        {
            // No separator — treat header as paragraph
            return new ParagraphBlock
            {
                Inlines = _inlineParser.ParseInlines(headerText),
                LineStart = firstLine.LineNumber,
                LineEnd = firstLine.LineNumber
            };
        }

        var separatorLine = separatorPeek.Content.Trim();
        if (!TableParserHelpers.IsTableSeparator(separatorLine))
        {
            // Not a table — treat header as paragraph
            return new ParagraphBlock
            {
                Inlines = _inlineParser.ParseInlines(headerText),
                LineStart = firstLine.LineNumber,
                LineEnd = firstLine.LineNumber
            };
        }

        // Parse headers
        var headers = ParseTableRow(headerText);
        var alignments = ParseTableAlignments(separatorLine);

        reader.ReadLine(); // consume separator

        var rows = new List<List<string>>();
        var endLine = separatorPeek.LineNumber;

        // Parse data rows
        while (reader.HasMore)
        {
            var rowPeek = reader.PeekLine();
            if (rowPeek is null || string.IsNullOrWhiteSpace(rowPeek.Content)) break;
            var rowStripped = rowPeek.Content.Trim();
            if (!rowStripped.Contains('|')) break;

            rows.Add(ParseTableRow(rowStripped));
            reader.ReadLine();
            endLine = rowPeek.LineNumber;
        }

        return new TableBlock
        {
            Headers = headers,
            Rows = rows,
            Alignments = alignments,
            LineStart = firstLine.LineNumber,
            LineEnd = endLine
        };
    }

    private static List<string> ParseTableRow(string row)
    {
        var trimmed = row.Trim('|').Trim();
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static List<TableBlock.TableAlignment> ParseTableAlignments(string separator)
    {
        var cells = separator.Trim('|').Trim().Split('|');
        return cells.Select(cell =>
        {
            var trimmed = cell.Trim();
            var left = trimmed.StartsWith(':');
            var right = trimmed.EndsWith(':');
            if (left && right) return TableBlock.TableAlignment.Center;
            if (right) return TableBlock.TableAlignment.Right;
            return TableBlock.TableAlignment.Left;
        }).ToList();
    }

    #endregion

    #region HTML

    private Block? TryParseHtmlBlock(LineReader reader, LineInfo firstLine, int startIndex)
    {
        var lines = reader.PeekLines(200);
        if (lines.Count == 0)
            return null;

        var candidate = string.Join("\n", lines.Select(line => line.Content));
        if (!_htmlParser.TryParseBlockFragment(candidate, startIndex, out var fragment, out var consumedLineCount))
            return null;

        var endLine = firstLine.LineNumber;
        for (var i = 0; i < consumedLineCount; i++)
        {
            var consumed = reader.ReadLine();
            if (consumed is null)
                break;
            endLine = consumed.LineNumber;
        }

        return new HtmlBlock
        {
            Fragment = fragment,
            LineStart = firstLine.LineNumber,
            LineEnd = endLine,
            ColumnStart = startIndex
        };
    }

    #endregion

    #region Paragraph

    private Block ParseParagraph(LineReader reader, ParserState state)
    {
        var firstLine = reader.ReadLine()!;
        var text = firstLine.Content.TrimStart();
        var endLine = firstLine.LineNumber;

        // Collect continuation lines
        while (reader.HasMore)
        {
            var peeked = reader.PeekLine();
            if (peeked is null) break;
            if (string.IsNullOrWhiteSpace(peeked.Content)) break;

            // Stop at indented code block (4+ spaces)
            if (peeked.IndentLevel >= 4) break;

            var stripped = peeked.Content.TrimStart();

            // Stop at block-level elements
            if (stripped.Length > 0 && (
                stripped[0] == '#' ||
                stripped[0] == '>' ||
                stripped is ['`', '`', '`', ..] or ['~', '~', '~', ..] ||
                IsThematicBreak(stripped) ||
                (stripped[0] is '-' or '*' or '+' && stripped.Length > 1 && stripped[1] == ' ')
            ))
                break;

            if (TryParseOrderedListStart(stripped, out _)) break;

            text += "\n" + peeked.Content.TrimStart();
            reader.ReadLine();
            endLine = peeked.LineNumber;
        }

        // Check for setext heading
        if (reader.HasMore)
        {
            var nextPeek = reader.PeekLine();
            if (nextPeek is not null)
            {
                var nextTrimmed = nextPeek.Content.Trim();
                if (nextTrimmed.Length > 0 && nextTrimmed.All(c => c == '='))
                {
                    reader.ReadLine();
                    return new HeadingBlock
                    {
                        Level = 1,
                        Inlines = _inlineParser.ParseInlines(text.Replace("\n", " ")),
                        LineStart = firstLine.LineNumber,
                        LineEnd = nextPeek.LineNumber
                    };
                }
                if (nextTrimmed.Length > 0 && nextTrimmed.All(c => c == '-'))
                {
                    reader.ReadLine();
                    return new HeadingBlock
                    {
                        Level = 2,
                        Inlines = _inlineParser.ParseInlines(text.Replace("\n", " ")),
                        LineStart = firstLine.LineNumber,
                        LineEnd = nextPeek.LineNumber
                    };
                }
            }
        }

        return new ParagraphBlock
        {
            Inlines = _inlineParser.ParseInlines(NormalizeParagraphSoftBreaks(text)),
            LineStart = firstLine.LineNumber,
            LineEnd = endLine
        };
    }

    private static string NormalizeParagraphSoftBreaks(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 1) return TrimLineBreakMarker(lines[0]);

        var normalized = new StringBuilder(TrimLineBreakMarker(lines[0]));
        for (var i = 1; i < lines.Length; i++)
        {
            normalized.Append(ShouldPreserveSoftBreak(lines[i - 1], lines[i]) ? '\n' : ' ');
            normalized.Append(TrimLineBreakMarker(lines[i]));
        }

        return normalized.ToString();
    }

    private static bool ShouldPreserveSoftBreak(string before, string after) =>
        HasHardLineBreak(before) ||
        IsStandaloneImageLine(before) ||
        IsStandaloneImageLine(after) ||
        IsCjkLineBreak(before, after);

    private static bool HasHardLineBreak(string line)
    {
        var trailingSpaces = 0;
        for (var i = line.Length - 1; i >= 0 && line[i] == ' '; i--)
            trailingSpaces++;

        return trailingSpaces >= 2 || line.EndsWith('\\');
    }

    private static string TrimLineBreakMarker(string line)
    {
        var trimmed = line.TrimEnd();
        return line.EndsWith('\\') ? trimmed.TrimEnd('\\') : trimmed;
    }

    private static bool IsStandaloneImageLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("![", StringComparison.Ordinal) ||
               trimmed.StartsWith("<img", StringComparison.OrdinalIgnoreCase) ||
               (trimmed.StartsWith("<a", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("<img", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCjkLineBreak(string before, string after) =>
        ContainsCjk(before) && ContainsCjk(after);

    private static bool ContainsCjk(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u3400' and <= '\u4DBF' ||
                c is >= '\u4E00' and <= '\u9FFF' ||
                c is >= '\uF900' and <= '\uFAFF')
                return true;
        }

        return false;
    }

    #endregion
}
