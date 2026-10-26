using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Rendering;
using Xunit;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

/// <summary>
/// End-to-end scroll-sync coverage against the real repository README, complementing
/// the synthetic-document tests in MarkdownEditorPreviewTests. Guards the reported
/// failure mode: after reading far down the preview, clicking a source paragraph that
/// sits above the viewport must bring that paragraph into view, not its next block.
/// </summary>
public sealed class ScrollSyncIntegrationTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WpfMarkdownEditor.sln")))
        {
            dir = dir.Parent;
            Assert.NotNull(dir);
        }
        return dir.FullName;
    }

    [Fact]
    public void RealReadme_ClickIntroParagraph_AfterScrollingDown_ShowsClickedParagraph()
    {
        WpfTestHost.Run(() =>
        {
            var readmeLines = File.ReadAllLines(Path.Combine(FindRepoRoot(), "README.md"));
            var introLine = Array.FindIndex(readmeLines, l => l.Contains("WPF Markdown workspace"));
            Assert.True(introLine >= 0, "README intro paragraph line not found.");

            using var editor = new MarkdownEditor();
            var window = new Window { Content = editor, Width = 1774, Height = 1212 };
            window.Show();
            try
            {
                editor.Markdown = File.ReadAllText(Path.Combine(FindRepoRoot(), "README.md"));
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 5));
                window.UpdateLayout();

                var scrollViewer = FindPreviewScrollViewer(editor.PreviewViewer)!;
                var document = editor.PreviewViewer.Document!;

                scrollViewer.ScrollToEnd();
                window.UpdateLayout();
                Assert.True(scrollViewer.VerticalOffset > 0);

                editor.TextBox.Select(editor.TextBox.GetCharacterIndexFromLineIndex(introLine), 0);

                Assert.True(WaitUntil(() =>
                {
                    var block = FindBlockForLine(document, introLine + 1);
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

    private static WpfBlock? FindBlockForLine(FlowDocument document, int line)
    {
        foreach (WpfBlock block in document.Blocks)
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

    [Fact]
    public void Click_LineAfterSoftWrappedParagraph_ReportsSourceLineNotDisplayLine()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                // The first paragraph soft-wraps into several display rows. A click on
                // the line after it must report the source line, not the display row.
                var longLine = string.Concat(Enumerable.Repeat("soft wrapped paragraph word ", 80));
                var markdown = longLine + "\n\nsecond paragraph\n\nthird paragraph";
                editor.Markdown = markdown;
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count == 3));
                window.UpdateLayout();

                // Caret at the start of the text stays on source line 1 despite wrapping.
                editor.TextBox.Select(0, 0);
                Assert.Equal(1, editor.TextBox.GetLineIndexFromCharacterIndex(0) + 1);

                var thirdLineOffset = editor.TextBox.Text.IndexOf("third paragraph", StringComparison.Ordinal);
                var caretLine = SourceLineMapper.GetSourceLine(editor.TextBox, thirdLineOffset);
                Assert.Equal(5, caretLine);

                // Display-line count must exceed the source-line count here, which is
                // exactly the mismatch this mapper corrects.
                Assert.True(editor.TextBox.LineCount > SourceLineMapper.GetSourceLineCount(editor.TextBox));

                // Round-trip: offset -> source line -> offset is stable.
                var roundTrip = SourceLineMapper.GetOffsetOfSourceLine(editor.TextBox, caretLine);
                Assert.Equal(thirdLineOffset, roundTrip);
            }
            finally
            {
                window.Close();
            }
        });
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
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        return condition();
    }
}
