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
}
