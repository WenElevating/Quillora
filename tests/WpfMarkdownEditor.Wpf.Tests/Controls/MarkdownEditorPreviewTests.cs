using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    public void ViewportOptions_DefaultsMatchReadingFirstExperience()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();

            Assert.True(editor.EnableScrollSync);
            Assert.False(editor.HighlightActiveBlock);
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
    public void HighlightActiveBlock_DoesNotTintHeadings()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor { HighlightActiveBlock = true };
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "# Heading\n\nbody paragraph";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count == 2));

                editor.GoToLine(1);
                window.UpdateLayout();

                var heading = editor.PreviewViewer.Document!.Blocks
                    .Cast<System.Windows.Documents.Block>()
                    .First();
                Assert.Null(heading.Background);
            }
            finally
            {
                window.Close();
            }
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
                editor.HighlightActiveBlock = true;
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

    [Fact]
    public void GoToLine_ScrollSync_BringsTargetBlockIntoView()
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
                window.UpdateLayout();

                var document = editor.PreviewViewer.Document!;
                var block = FindBlockForLine(document, 141);
                Assert.NotNull(block);
                var scrollViewer = FindPreviewScrollViewer(editor.PreviewViewer)!;
                var rect = block!.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Assert.True(
                    rect.Top >= 0 && rect.Top < scrollViewer.ViewportHeight,
                    $"blockTop={rect.Top:F1} offset={scrollViewer.VerticalOffset:F1} viewport={scrollViewer.ViewportHeight:F1}");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ScrollSync_TargetAboveViewport_AlignsTargetBlockInsideViewport()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                // One very long source line soft-wraps into a tall block, so the target
                // paragraph on line 3 sits far below the document top. Document-relative
                // rects (used by GetCharacterRect for off-screen blocks) then exceed the
                // viewport height, which keeps this assertion meaningful in both of its
                // coordinate flavors.
                var longLine = string.Concat(Enumerable.Repeat("long paragraph word ", 400));
                var markdown = longLine + "\n\ntarget paragraph\n\n"
                    + string.Join("\n\n", Enumerable.Range(0, 120).Select(i => $"tail paragraph {i}"));
                editor.Markdown = markdown;
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 100));
                window.UpdateLayout();

                var scrollViewer = FindPreviewScrollViewer(editor.PreviewViewer)!;
                scrollViewer.ScrollToEnd();
                window.UpdateLayout();
                Assert.True(scrollViewer.VerticalOffset > 0);

                // Place the caret on source line 3 ("target paragraph"). Display-line
                // indexes cannot be used here: the long first line wraps into many rows.
                var targetOffset = editor.TextBox.Text.IndexOf("target paragraph", StringComparison.Ordinal);
                Assert.True(targetOffset > 0);
                editor.TextBox.Select(targetOffset, 0);

                Assert.True(WaitUntil(() =>
                {
                    var block = FindBlockForLine(editor.PreviewViewer.Document!, 3);
                    if (block is null)
                        return false;

                    var rect = block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    return !rect.IsEmpty
                        && rect.Top >= 0
                        && rect.Bottom > 0
                        && rect.Top < scrollViewer.ViewportHeight;
                }));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ScrollSync_TargetJustAboveViewport_AlignsTargetBlockInsideViewport()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                var markdown = "intro\n\ntarget paragraph\n\n"
                    + string.Join("\n\n", Enumerable.Range(0, 40).Select(i => $"paragraph {i}"));
                editor.Markdown = markdown;
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count == 42));
                window.UpdateLayout();

                var scrollViewer = FindPreviewScrollViewer(editor.PreviewViewer)!;
                var document = editor.PreviewViewer.Document!;
                var target = FindBlockForLine(document, 3)!;

                // Park the viewport just below the target block so its bottom edge is
                // a few pixels above the viewport top: the exact state from the bug
                // report where the clicked paragraph is not readable in the preview.
                var targetRect = target.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Assert.False(targetRect.IsEmpty);
                scrollViewer.ScrollToVerticalOffset(targetRect.Bottom + 6);
                window.UpdateLayout();
                Assert.True(scrollViewer.VerticalOffset > 0);

                var targetOffset = editor.TextBox.Text.IndexOf("target paragraph", StringComparison.Ordinal);
                Assert.True(targetOffset > 0);
                editor.TextBox.Select(targetOffset, 0);

                Assert.True(WaitUntil(() =>
                {
                    var rect = target.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    return !rect.IsEmpty
                        && rect.Top >= 0
                        && rect.Bottom > 0
                        && rect.Top < scrollViewer.ViewportHeight;
                }));
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

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 8000)
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
