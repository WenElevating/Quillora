using WpfMarkdownEditor.Wpf.Rendering;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class SourceLineIndexTests
{
    [Fact]
    public void Create_LfDocument_TracksLineCount()
    {
        var index = SourceLineIndex.Create("one\ntwo\nthree");

        Assert.Equal(3, index.LineCount);
    }

    [Fact]
    public void Create_CrLfDocument_TracksLineCount()
    {
        var index = SourceLineIndex.Create("one\r\ntwo\r\nthree");

        Assert.Equal(3, index.LineCount);
    }

    [Fact]
    public void Create_TrailingNewline_DoesNotAddEmptyLine()
    {
        var index = SourceLineIndex.Create("one\ntwo\n");

        Assert.Equal(2, index.LineCount);
    }

    [Fact]
    public void Create_EmptyDocument_HasSingleEmptyLine()
    {
        var index = SourceLineIndex.Create(string.Empty);

        Assert.Equal(1, index.LineCount);
    }

    [Fact]
    public void TryComputeHash_SameContent_ReturnsSameHash()
    {
        var index = SourceLineIndex.Create("alpha\nbeta\ngamma");

        Assert.True(index.TryComputeHash(1, 3, out var first));
        Assert.True(index.TryComputeHash(1, 3, out var second));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryComputeHash_LfAndCrLfDocuments_ReturnSameHash()
    {
        var lf = SourceLineIndex.Create("alpha\nbeta\ngamma");
        var crlf = SourceLineIndex.Create("alpha\r\nbeta\r\ngamma");

        Assert.True(lf.TryComputeHash(1, 3, out var lfHash));
        Assert.True(crlf.TryComputeHash(1, 3, out var crlfHash));

        Assert.Equal(lfHash, crlfHash);
    }

    [Fact]
    public void TryComputeHash_WhitespaceChange_ChangesHash()
    {
        var before = SourceLineIndex.Create("alpha beta");
        var after = SourceLineIndex.Create("alpha  beta");

        Assert.True(before.TryComputeHash(1, 1, out var beforeHash));
        Assert.True(after.TryComputeHash(1, 1, out var afterHash));

        Assert.NotEqual(beforeHash, afterHash);
    }

    [Fact]
    public void TryComputeHash_LineBreaksParticipate_ChangesHash()
    {
        var joined = SourceLineIndex.Create("alphabeta");
        var split = SourceLineIndex.Create("alpha\nbeta");

        Assert.True(joined.TryComputeHash(1, 1, out var joinedHash));
        Assert.True(split.TryComputeHash(1, 2, out var splitHash));

        Assert.NotEqual(joinedHash, splitHash);
    }

    [Fact]
    public void TryComputeHash_DifferentLineRanges_Differ()
    {
        var index = SourceLineIndex.Create("alpha\nbeta\ngamma");

        Assert.True(index.TryComputeHash(1, 1, out var firstLine));
        Assert.True(index.TryComputeHash(2, 2, out var secondLine));

        Assert.NotEqual(firstLine, secondLine);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(1, 9)]
    public void TryComputeHash_InvalidRange_ReturnsFalse(int lineStart, int lineEnd)
    {
        var index = SourceLineIndex.Create("alpha\nbeta\ngamma");

        Assert.False(index.TryComputeHash(lineStart, lineEnd, out _));
    }
}
