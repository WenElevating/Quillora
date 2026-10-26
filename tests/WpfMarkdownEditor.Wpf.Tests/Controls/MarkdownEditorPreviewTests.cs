using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Rendering;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

public sealed class MarkdownEditorPreviewTests
{
    [Fact]
    public void PreviewRendered_EventCarriesMarkdownAndParsedBlocks()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            PreviewRenderedEventArgs? received = null;
            editor.PreviewRendered += (_, e) => received = e;

            editor.Markdown = "# One\n\ntext\n\n## Two";

            Assert.True(WaitUntil(() => received is not null));
            Assert.Equal(editor.Markdown, received!.Markdown);
            Assert.Equal(2, received.Blocks.OfType<HeadingBlock>().Count());
        });
    }

    [Fact]
    public void ViewportOptions_DefaultToEnabled()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();

            Assert.True(editor.EnableScrollSync);
            Assert.True(editor.HighlightActiveBlock);
        });
    }

    [Fact]
    public void GoToLine_PlacesCaretOnLineStart()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "first\nsecond\nthird";
                window.UpdateLayout();

                editor.GoToLine(2);

                var expected = editor.TextBox.GetCharacterIndexFromLineIndex(1);
                Assert.Equal(expected, editor.TextBox.SelectionStart);
                Assert.Equal(0, editor.TextBox.SelectionLength);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void GoToLine_OutOfRange_ClampsToLastLine()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "first\nsecond";
                window.UpdateLayout();

                editor.GoToLine(9999);

                var expected = editor.TextBox.GetCharacterIndexFromLineIndex(1);
                Assert.Equal(expected, editor.TextBox.SelectionStart);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void LargeMarkdown_UsesProgressivePreviewAndCompletes()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var sawPartial = false;
            var sawComplete = false;
            editor.PreviewRendered += (_, e) =>
            {
                if (e.IsComplete)
                    sawComplete = true;
                else
                    sawPartial = true;
            };

            var markdown = string.Join(
                "\n\n",
                Enumerable.Range(0, 1000).Select(i => $"paragraph {i}"));
            editor.Markdown = markdown;

            Assert.True(WaitUntil(() => sawPartial));
            Assert.True(WaitUntil(() => sawComplete));
            Assert.NotNull(editor.PreviewViewer.Document);
            Assert.Equal(1000, editor.PreviewViewer.Document!.Blocks.Count);
        });
    }

    [Fact]
    public void GoToLine_WithScrollSync_ScrollsPreviewAndHighlightsCurrentBlock()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                var markdown = string.Join("\n\n", Enumerable.Range(0, 80).Select(i => $"paragraph {i}"));
                editor.Markdown = markdown;
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count == 80));
                window.UpdateLayout();

                editor.GoToLine(141);

                Assert.True(WaitUntil(
                    () => FindPreviewScrollViewer(editor.PreviewViewer) is { VerticalOffset: > 100 }));
                var block = FindBlockForLine(editor.PreviewViewer.Document!, 141);
                Assert.NotNull(block);
                Assert.NotNull(block!.Background);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static System.Windows.Documents.Block? FindBlockForLine(
        System.Windows.Documents.FlowDocument document,
        int line)
    {
        foreach (var block in document.Blocks)
        {
            if (RenderedBlockMetadata.TryGet(block, out var info)
                && info.SourceStartLine <= line
                && line <= info.SourceEndLine)
            {
                return block;
            }
        }

        return null;
    }

    private static ScrollViewer? FindPreviewScrollViewer(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < childCount; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }

        return null;
    }

    private static Window ShowInWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 640, Height = 420 };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }

        return condition();
    }
}
