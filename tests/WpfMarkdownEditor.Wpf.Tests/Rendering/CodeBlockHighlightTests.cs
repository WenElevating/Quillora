using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.SyntaxHighlighting;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Rendering;

public sealed class CodeBlockHighlightTests
{
    private readonly MarkdownParser _parser = new();

    [Fact]
    public void VisibleCodeBlock_GetsSyntaxHighlighting()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            try
            {
                var paragraph = RenderSingleCodeBlock(viewer);
                using var scheduler = new CodeBlockHighlightScheduler(viewer, () => { });

                scheduler.OnDocumentRendered();

                Assert.True(WaitUntil(() => paragraph.Inlines.Count > 1));
                Assert.Contains(
                    paragraph.Inlines.OfType<Run>(),
                    run => run.Foreground is SolidColorBrush);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnBlocksAppended_HighlightsVisibleCodeBlock()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            try
            {
                var renderer = new FlowDocumentRenderer(EditorTheme.Light, null, new SyntaxHighlighter());
                viewer.Document = renderer.Render(_parser.Parse("```csharp\nvar value = 1; // comment\n```"));
                window.UpdateLayout();
                var paragraph = FindCodeParagraph(viewer.Document!);
                var section = (Section)viewer.Document!.Blocks.First();
                using var scheduler = new CodeBlockHighlightScheduler(viewer, () => { });

                scheduler.OnBlocksAppended([section]);

                Assert.True(WaitUntil(() => paragraph.Inlines.Count > 1));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OffscreenCodeBlock_StaysPlainUntilScrolledIntoView()    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            try
            {
                var markdown = string.Join(
                    "\n\n",
                    Enumerable.Range(0, 80).Select(i => $"paragraph {i}")) +
                    "\n\n```csharp\nvar value = 1;\n```";
                var renderer = new FlowDocumentRenderer(EditorTheme.Light, null, new SyntaxHighlighter());
                viewer.Document = renderer.Render(_parser.Parse(markdown));
                window.UpdateLayout();

                var paragraph = FindCodeParagraph(viewer.Document!);
                using var scheduler = new CodeBlockHighlightScheduler(viewer, () => { });
                scheduler.OnDocumentRendered();
                PumpDispatcher();

                Assert.Single(paragraph.Inlines);

                var scrollViewer = FindScrollViewer(viewer);
                Assert.NotNull(scrollViewer);
                scrollViewer!.ScrollToEnd();
                window.UpdateLayout();

                Assert.True(WaitUntil(() => paragraph.Inlines.Count > 1));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private Paragraph RenderSingleCodeBlock(FlowDocumentScrollViewer viewer)
    {
        var renderer = new FlowDocumentRenderer(EditorTheme.Light, null, new SyntaxHighlighter());
        viewer.Document = renderer.Render(_parser.Parse("```csharp\nvar value = 1; // comment\n```"));
        return FindCodeParagraph(viewer.Document!);
    }

    private static Paragraph FindCodeParagraph(FlowDocument document) =>
        document.Blocks.OfType<Section>()
            .SelectMany(section => section.Blocks.OfType<Paragraph>())
            .First();

    private static Window ShowInWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 720, Height = 420 };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
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

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;

            PumpDispatcher();
            Thread.Sleep(10);
        }

        return condition();
    }
}
