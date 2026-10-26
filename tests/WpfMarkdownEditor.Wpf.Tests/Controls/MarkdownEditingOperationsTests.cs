using WpfMarkdownEditor.Wpf.Controls;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

public sealed class MarkdownEditingOperationsTests
{
    private static readonly string NewLine = Environment.NewLine;

    [Fact]
    public void TryHandleEnter_NonEmptyUnorderedItem_ContinuesMarker()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("- item", 6, out var operation);

        Assert.True(handled);
        Assert.Equal("- item" + NewLine + "- ", operation.Text);
        Assert.Equal(operation.Text.Length, operation.SelectionStart);
        Assert.Equal(0, operation.SelectionLength);
    }

    [Fact]
    public void TryHandleEnter_OrderedItem_IncrementsMarker()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("1. item", 7, out var operation);

        Assert.True(handled);
        Assert.Equal("1. item" + NewLine + "2. ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_IndentedItem_PreservesIndent()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("  - item", 8, out var operation);

        Assert.True(handled);
        Assert.Equal("  - item" + NewLine + "  - ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_EmptyItem_ExitsList()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("- ", 2, out var operation);

        Assert.True(handled);
        Assert.Equal(NewLine, operation.Text);
        Assert.Equal(NewLine.Length, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleEnter_CaretBeforeMarker_ReturnsFalse()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("- item", 0, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleEnter_NotListLine_ReturnsFalse()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("plain text", 5, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleEnter_EmptyText_ReturnsFalse()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter(string.Empty, 0, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleEnter_CaretInLaterLine_UsesCurrentLine()
    {
        var text = "- first" + NewLine + "- second";
        var caret = text.Length;

        var handled = MarkdownEditingOperations.TryHandleEnter(text, caret, out var operation);

        Assert.True(handled);
        Assert.Equal(text + NewLine + "- ", operation.Text);
    }

    [Fact]
    public void TryHandleTab_ListLine_Indents()
    {
        var handled = MarkdownEditingOperations.TryHandleTab("- item", 2, shift: false, out var operation);

        Assert.True(handled);
        Assert.Equal("  - item", operation.Text);
        Assert.Equal(4, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleTab_ShiftWithLeadingSpaces_Outdents()
    {
        var handled = MarkdownEditingOperations.TryHandleTab("    - item", 7, shift: true, out var operation);

        Assert.True(handled);
        Assert.Equal("  - item", operation.Text);
        Assert.Equal(5, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleTab_ShiftWithoutLeadingSpaces_HandledWithoutChange()
    {
        var handled = MarkdownEditingOperations.TryHandleTab("- item", 2, shift: true, out var operation);

        Assert.True(handled);
        Assert.Equal("- item", operation.Text);
        Assert.Equal(2, operation.SelectionStart);
        Assert.Equal(0, operation.SelectionLength);
    }

    [Fact]
    public void TryHandleTab_NotListLine_ReturnsFalse()
    {
        var handled = MarkdownEditingOperations.TryHandleTab("plain text", 5, shift: false, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleTab_CaretInLaterLine_IndentsThatLine()
    {
        var text = "paragraph" + NewLine + "- item";
        var caret = text.Length;

        var handled = MarkdownEditingOperations.TryHandleTab(text, caret, shift: false, out var operation);

        Assert.True(handled);
        Assert.Equal("paragraph" + NewLine + "  - item", operation.Text);
        Assert.Equal(caret + 2, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleEnter_Blockquote_ContinuesQuote()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("> quote", 7, out var operation);

        Assert.True(handled);
        Assert.Equal("> quote" + NewLine + "> ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_NestedBlockquote_KeepsDepth()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("> > quote", 9, out var operation);

        Assert.True(handled);
        Assert.Equal("> > quote" + NewLine + "> > ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_BlockquoteWithoutSpace_NormalizesContinuation()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter(">quote", 6, out var operation);

        Assert.True(handled);
        Assert.Equal(">quote" + NewLine + "> ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_TaskItem_AddsUncheckedTask()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("- [x] done", 10, out var operation);

        Assert.True(handled);
        Assert.Equal("- [x] done" + NewLine + "- [ ] ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_EmptyTaskItem_ExitsTaskList()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("- [ ] ", 6, out var operation);

        Assert.True(handled);
        Assert.Equal(NewLine, operation.Text);
        Assert.Equal(NewLine.Length, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleEnter_EmptyBlockquote_ExitsQuote()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("> ", 2, out var operation);

        Assert.True(handled);
        Assert.Equal(NewLine, operation.Text);
    }

    [Fact]
    public void TryHandleEnter_OrderedParenMarker_IncrementsAndKeepsDelimiter()
    {
        var handled = MarkdownEditingOperations.TryHandleEnter("3) item", 7, out var operation);

        Assert.True(handled);
        Assert.Equal("3) item" + NewLine + "4) ", operation.Text);
    }

    [Fact]
    public void TryHandleEnter_InsideFencedCode_ReturnsFalse()
    {
        var text = "```" + NewLine + "- item";

        var handled = MarkdownEditingOperations.TryHandleEnter(text, text.Length, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleTab_MultiLineSelection_IndentsEveryLineAndPreservesSelection()
    {
        var text = "- one" + NewLine + "- two";
        var selectedLength = text.Length;

        var handled = MarkdownEditingOperations.TryHandleTab(
            text,
            selectionStart: 0,
            selectionLength: selectedLength,
            shift: false,
            out var operation);

        Assert.True(handled);
        Assert.Equal("  - one" + NewLine + "  - two", operation.Text);
        Assert.Equal(2, operation.SelectionStart);
        Assert.Equal(operation.Text.Length - 2, operation.SelectionLength);
    }

    [Fact]
    public void TryHandleTab_ShiftMultiLineSelection_OutdentsEveryLine()
    {
        var text = "  - one" + NewLine + "  - two";

        var handled = MarkdownEditingOperations.TryHandleTab(
            text,
            selectionStart: 0,
            selectionLength: text.Length,
            shift: true,
            out var operation);

        Assert.True(handled);
        Assert.Equal("- one" + NewLine + "- two", operation.Text);
        Assert.Equal(0, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleTab_MultiLineInsideFence_ReturnsFalse()
    {
        var text = "```" + NewLine + "- one" + NewLine + "- two";

        var handled = MarkdownEditingOperations.TryHandleTab(
            text,
            4,
            text.Length - 4,
            shift: false,
            out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleTab_PlainMultiLineSelection_ReturnsFalse()
    {
        var text = "plain one" + NewLine + "plain two";

        var handled = MarkdownEditingOperations.TryHandleTab(
            text,
            0,
            text.Length,
            shift: false,
            out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleBackspace_ListMarker_RemovesMarkerAndKeepsContent()
    {
        var handled = MarkdownEditingOperations.TryHandleBackspace("- item", 2, 0, out var operation);

        Assert.True(handled);
        Assert.Equal("item", operation.Text);
        Assert.Equal(0, operation.SelectionStart);
    }

    [Fact]
    public void TryHandleBackspace_BlockquoteMarker_RemovesMarker()
    {
        var handled = MarkdownEditingOperations.TryHandleBackspace("> quote", 2, 0, out var operation);

        Assert.True(handled);
        Assert.Equal("quote", operation.Text);
    }

    [Fact]
    public void TryHandleBackspace_TaskMarker_RemovesWholePrefix()
    {
        var handled = MarkdownEditingOperations.TryHandleBackspace("- [ ] task", 6, 0, out var operation);

        Assert.True(handled);
        Assert.Equal("task", operation.Text);
    }

    [Fact]
    public void TryHandleBackspace_InsideContent_ReturnsFalse()
    {
        var handled = MarkdownEditingOperations.TryHandleBackspace("plain text", 5, 0, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleBackspace_WithSelection_ReturnsFalse()
    {
        var handled = MarkdownEditingOperations.TryHandleBackspace("- item", 0, 4, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleBackspace_InsideFencedCode_ReturnsFalse()
    {
        var text = "```" + NewLine + "- item";

        var handled = MarkdownEditingOperations.TryHandleBackspace(text, text.Length, 0, out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryHandleEnter_CrLfDocument_ContinuesList()
    {
        var text = "- one" + NewLine + "- two";

        var handled = MarkdownEditingOperations.TryHandleEnter(text, text.Length, out var operation);

        Assert.True(handled);
        Assert.Equal(text + NewLine + "- ", operation.Text);
    }
}
