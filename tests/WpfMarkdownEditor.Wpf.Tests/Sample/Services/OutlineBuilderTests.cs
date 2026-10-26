using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Sample.Services;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Sample.Services;

public sealed class OutlineBuilderTests
{
    private readonly MarkdownParser _parser = new();

    [Fact]
    public void Build_Headings_ReturnsLevelTextAndLine()
    {
        const string markdown = "# One\n\ntext\n\n### Three ###\n\n## Two";

        var entries = OutlineBuilder.Build(markdown, _parser.Parse(markdown));

        Assert.Equal(3, entries.Count);
        Assert.Equal(new OutlineEntry(1, "One", 1), entries[0]);
        Assert.Equal(new OutlineEntry(3, "Three", 5), entries[1]);
        Assert.Equal(new OutlineEntry(2, "Two", 7), entries[2]);
    }

    [Fact]
    public void Build_EmptyHeading_UsesPlaceholder()
    {
        const string markdown = "# ";

        var entries = OutlineBuilder.Build(markdown, _parser.Parse(markdown));

        var entry = Assert.Single(entries);
        Assert.Equal("…", entry.Text);
    }

    [Fact]
    public void Build_NoHeadings_ReturnsEmpty()
    {
        const string markdown = "plain text\n\n- list";

        var entries = OutlineBuilder.Build(markdown, _parser.Parse(markdown));

        Assert.Empty(entries);
    }

    [Fact]
    public void Build_CrLfDocument_UsesSourceLineNumbers()
    {
        const string markdown = "# One\r\n\r\n## Two";

        var entries = OutlineBuilder.Build(markdown, _parser.Parse(markdown));

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Line);
        Assert.Equal(3, entries[1].Line);
    }
}
