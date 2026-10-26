using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Core.Parsing.Inlines;
using Xunit;

namespace WpfMarkdownEditor.Core.Tests.Parsing;

public sealed class TaskListParsingTests
{
    private readonly MarkdownParser _parser = new();

    [Fact]
    public void Parse_UnorderedTaskList_StripsMarkersAndSetsState()
    {
        var blocks = _parser.Parse("- [ ] todo\n- [x] done");

        var list = Assert.IsType<ListBlock>(Assert.Single(blocks));
        Assert.Equal(2, list.Items.Count);
        Assert.True(list.Items[0].IsTask);
        Assert.False(list.Items[0].IsTaskChecked);
        Assert.True(list.Items[1].IsTask);
        Assert.True(list.Items[1].IsTaskChecked);

        var firstText = Assert.IsType<ParagraphBlock>(Assert.Single(list.Items[0].Blocks));
        var firstInline = Assert.IsType<TextInline>(Assert.Single(firstText.Inlines));
        Assert.Equal("todo", firstInline.Content);

        var secondText = Assert.IsType<ParagraphBlock>(Assert.Single(list.Items[1].Blocks));
        var secondInline = Assert.IsType<TextInline>(Assert.Single(secondText.Inlines));
        Assert.Equal("done", secondInline.Content);
    }

    [Fact]
    public void Parse_OrderedTaskList_DetectsTask()
    {
        var blocks = _parser.Parse("1. [X] done");

        var list = Assert.IsType<ListBlock>(Assert.Single(blocks));
        var item = Assert.Single(list.Items);
        Assert.True(item.IsTask);
        Assert.True(item.IsTaskChecked);
    }

    [Fact]
    public void Parse_BracketTextWithoutTaskSyntax_RemainsText()
    {
        var blocks = _parser.Parse("- [link] text");

        var list = Assert.IsType<ListBlock>(Assert.Single(blocks));
        var item = Assert.Single(list.Items);
        Assert.False(item.IsTask);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(item.Blocks));
        Assert.Contains("[link] text", paragraph.Inlines.OfType<TextInline>().Select(static inline => inline.Content));
    }
}
