using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Controls;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

/// <summary>
/// Covers the Typora-style integrated pane: the rendered document fills the pane, a click
/// swaps the clicked block into a markdown source editor, and committing writes it back.
/// </summary>
public sealed class IntegratedModeTests
{
    [Fact]
    public void IntegratedMode_RendersDocumentAndCoversSplitView()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "# Title\n\nfirst paragraph\n\nsecond paragraph";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                editor.IntegratedMode = true;

                Assert.True(WaitUntil(() => editor.IntegratedViewerForTest.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                Assert.Equal(Visibility.Visible, editor.IntegratedModePanel.Visibility);
                Assert.NotNull(editor.BlockEditorForTest);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void IntegratedMode_OpenBlock_ShowsItsMarkdownSource()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "# Title\n\nfirst paragraph\n\nsecond paragraph";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                editor.IntegratedMode = true;
                Assert.True(WaitUntil(() => editor.IntegratedViewerForTest.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                var blockEditor = editor.BlockEditorForTest!;
                Assert.True(blockEditor.TryOpenBlockForTest(3));
                Assert.True(blockEditor.IsEditing);

                // Line 3 is the first paragraph; the editor shows its raw markdown.
                Assert.Equal("first paragraph", blockEditor.EditorTextForTest);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void IntegratedMode_EditBlock_WritesBackToMarkdown()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "# Title\n\nfirst paragraph\n\nsecond paragraph";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                editor.IntegratedMode = true;
                Assert.True(WaitUntil(() => editor.IntegratedViewerForTest.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                var blockEditor = editor.BlockEditorForTest!;
                Assert.True(blockEditor.TryOpenBlockForTest(3));
                blockEditor.SetEditorTextForTest("changed **paragraph**");
                blockEditor.CloseEditor(commit: true);

                Assert.Contains("changed **paragraph**", editor.Markdown);
                Assert.DoesNotContain("first paragraph", editor.Markdown);
                Assert.Contains("second paragraph", editor.Markdown);
                Assert.Contains("# Title", editor.Markdown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void IntegratedMode_HeadingBlock_KeepsItsMarkerWhenCommitted()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "# Title\n\nbody";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                editor.IntegratedMode = true;
                Assert.True(WaitUntil(() => editor.IntegratedViewerForTest.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                var blockEditor = editor.BlockEditorForTest!;
                Assert.True(blockEditor.TryOpenBlockForTest(1));
                Assert.Equal("# Title", blockEditor.EditorTextForTest);

                blockEditor.SetEditorTextForTest("## Renamed");
                blockEditor.CloseEditor(commit: true);

                Assert.Contains("## Renamed", editor.Markdown);
                Assert.DoesNotContain("# Title", editor.Markdown.Replace("## Renamed", string.Empty));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void IntegratedMode_Disabled_RestoresSplitView()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "# Title\n\nbody";
                Assert.True(WaitUntil(() => editor.PreviewViewer.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                editor.IntegratedMode = true;
                Assert.True(WaitUntil(() => editor.IntegratedViewerForTest.Document?.Blocks.Count > 0));
                window.UpdateLayout();

                editor.IntegratedMode = false;
                window.UpdateLayout();

                Assert.Equal(Visibility.Collapsed, editor.IntegratedModePanel.Visibility);
                Assert.Null(editor.BlockEditorForTest);
                Assert.Equal(Visibility.Visible, editor.EditorTextBox.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ToggleIntegratedMode_Command_FlipsTheMode()
    {
        WpfTestHost.Run(() =>
        {
            using var editor = new MarkdownEditor();
            var window = ShowInWindow(editor);
            try
            {
                editor.Markdown = "body";
                window.UpdateLayout();
                Assert.False(editor.IntegratedMode);

                MarkdownEditorCommands.ToggleIntegratedMode.Execute(null, editor);
                Assert.True(editor.IntegratedMode);

                MarkdownEditorCommands.ToggleIntegratedMode.Execute(null, editor);
                Assert.False(editor.IntegratedMode);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window ShowInWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 900, Height = 700 };
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