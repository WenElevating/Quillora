using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;
using Xunit.Abstractions;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

public sealed class ProgressiveRenderingTests
{
    private readonly ITestOutputHelper _output;

    public ProgressiveRenderingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LargeDocument_RendersFirstChunkThenCompletes()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            PreviewRenderController? controller = null;
            PreviewRenderedEventArgs? firstEvent = null;
            PreviewRenderedEventArgs? finalEvent = null;
            bool? printableDuringFirstPaint = null;

            try
            {
                var markdown = BuildMarkdown(3000);
                controller = new PreviewRenderController(
                    viewer,
                    () => markdown,
                    () => EditorTheme.Light,
                    () => AppContext.BaseDirectory,
                    viewportController: null,
                    onRendered: args =>
                    {
                        if (args.IsComplete)
                        {
                            finalEvent = args;
                        }
                        else
                        {
                            firstEvent = args;
                            printableDuringFirstPaint = controller!.TryGetPrintablePreviewDocument(out _);
                        }
                    });

                controller.OnMarkdownChanged();
                var total = Stopwatch.StartNew();

                Assert.True(WaitUntil(() => firstEvent is not null));
                Assert.False(firstEvent!.IsComplete);
                Assert.False(printableDuringFirstPaint);
                Assert.Equal(3000, firstEvent.Blocks.Count);

                Assert.True(WaitUntil(() => finalEvent is not null));
                total.Stop();
                _output.WriteLine(
                    $"3000 blocks: first paint {controller.LastFirstPaintElapsedMs}ms, " +
                    $"complete {total.ElapsedMilliseconds}ms");
                Assert.True(finalEvent!.IsComplete);
                Assert.Equal(3000, viewer.Document!.Blocks.Count);
                Assert.Equal(3000, controller.RenderedPreviewBlockCount);
                Assert.False(controller.IsProgressiveRendering);
                Assert.True(controller.TryGetPrintablePreviewDocument(out _));
            }
            finally
            {
                controller?.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void SmallDocument_RendersSynchronously()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            PreviewRenderController? controller = null;
            var events = new List<PreviewRenderedEventArgs>();

            try
            {
                var markdown = BuildMarkdown(20);
                controller = new PreviewRenderController(
                    viewer,
                    () => markdown,
                    () => EditorTheme.Light,
                    () => AppContext.BaseDirectory,
                    viewportController: null,
                    onRendered: events.Add);

                controller.OnMarkdownChanged();

                Assert.True(WaitUntil(() => events.Count > 0));
                Assert.Single(events);
                Assert.True(events[0].IsComplete);
                Assert.False(controller.IsProgressiveRendering);
                Assert.Equal(20, viewer.Document!.Blocks.Count);
            }
            finally
            {
                controller?.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void EditTailDuringProgressive_PreservesRenderedPrefix()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            PreviewRenderController? controller = null;
            PreviewRenderedEventArgs? firstEvent = null;
            var markdown = BuildMarkdown(3000);

            try
            {
                controller = new PreviewRenderController(
                    viewer,
                    () => markdown,
                    () => EditorTheme.Light,
                    () => AppContext.BaseDirectory,
                    viewportController: null,
                    onRendered: args =>
                    {
                        if (!args.IsComplete)
                            firstEvent = args;
                    });

                controller.OnMarkdownChanged();
                Assert.True(WaitUntil(() => firstEvent is not null));
                Assert.True(controller.IsProgressiveRendering);

                var firstRenderedBlock = viewer.Document!.Blocks.Cast<System.Windows.Documents.Block>().First();
                markdown += "\n\nappended tail";
                controller.OnMarkdownChanged();

                Assert.True(WaitUntil(() => !controller.IsProgressiveRendering));
                Assert.Equal(3001, viewer.Document!.Blocks.Count);
                Assert.Same(
                    firstRenderedBlock,
                    viewer.Document.Blocks.Cast<System.Windows.Documents.Block>().First());
            }
            finally
            {
                controller?.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void EditHeadDuringProgressive_RestartsAndCompletes()
    {
        WpfTestHost.Run(() =>
        {
            var viewer = new FlowDocumentScrollViewer();
            var window = ShowInWindow(viewer);
            PreviewRenderController? controller = null;
            PreviewRenderedEventArgs? firstEvent = null;
            var markdown = BuildMarkdown(3000);

            try
            {
                controller = new PreviewRenderController(
                    viewer,
                    () => markdown,
                    () => EditorTheme.Light,
                    () => AppContext.BaseDirectory,
                    viewportController: null,
                    onRendered: args =>
                    {
                        if (!args.IsComplete)
                            firstEvent = args;
                    });

                controller.OnMarkdownChanged();
                Assert.True(WaitUntil(() => firstEvent is not null));
                var firstRenderedBlock = viewer.Document!.Blocks.Cast<System.Windows.Documents.Block>().First();

                markdown = "changed first paragraph\n\n" + markdown;
                controller.OnMarkdownChanged();

                Assert.True(WaitUntil(() => !controller.IsProgressiveRendering));
                Assert.Equal(3001, viewer.Document!.Blocks.Count);
                Assert.NotSame(
                    firstRenderedBlock,
                    viewer.Document.Blocks.Cast<System.Windows.Documents.Block>().First());
            }
            finally
            {
                controller?.Dispose();
                window.Close();
            }
        });
    }

    private static string BuildMarkdown(int paragraphs) =>
        string.Join("\n\n", Enumerable.Range(0, paragraphs).Select(i => $"paragraph {i}"));

    private static Window ShowInWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 720, Height = 480 };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
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
            Thread.Sleep(5);
        }

        return condition();
    }
}
