using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class EditorBrushCacheTests
{
    [Fact]
    public void Get_SameColor_ReturnsSameFrozenInstance()
    {
        var color = Color.FromRgb(0x12, 0x34, 0x56);

        var first = EditorBrushCache.Get(color);
        var second = EditorBrushCache.Get(color);

        Assert.Same(first, second);
        Assert.True(first.IsFrozen);
    }

    [Fact]
    public void Get_DifferentAlpha_ReturnsDifferentInstances()
    {
        var opaque = EditorBrushCache.Get(Color.FromArgb(0xFF, 0x12, 0x34, 0x56));
        var translucent = EditorBrushCache.Get(Color.FromArgb(0x18, 0x12, 0x34, 0x56));

        Assert.NotSame(opaque, translucent);
    }

    [Fact]
    public void Get_RepeatedCalls_DoNotGrowCache()
    {
        var color = Color.FromRgb(0x65, 0x43, 0x21);
        EditorBrushCache.Get(color);
        var count = EditorBrushCache.Count;

        for (var i = 0; i < 20; i++)
            EditorBrushCache.Get(color);

        Assert.Equal(count, EditorBrushCache.Count);
    }

    [Fact]
    public void Render_SiblingParagraphs_ShareCachedForegroundBrush()
    {
        WpfTestHost.Run(() =>
        {
            var renderer = new FlowDocumentRenderer(EditorTheme.Light);
            var document = renderer.Render(new MarkdownParser().Parse("alpha\n\nbeta"));
            var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

            Assert.Equal(2, paragraphs.Count);
            Assert.Same(paragraphs[0].Foreground, paragraphs[1].Foreground);
            Assert.Same(EditorBrushCache.Get(EditorTheme.Light.ForegroundColor), paragraphs[0].Foreground);
        });
    }
}
