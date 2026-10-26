using System.Windows;
using System.Windows.Controls;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Theming;
using Xunit;

namespace WpfMarkdownEditor.Wpf.Tests.Controls;

public sealed class PreviewViewportMappingTests
{
    [Fact]
    public void CaptureAnchor_UsesCaretLine()
    {
        WpfTestHost.Run(() =>
        {
            var textBox = new TextBox { Text = "first\nsecond\nthird" };
            var window = ShowInWindow(textBox);
            try
            {
                textBox.CaretIndex = textBox.Text.Length;
                var controller = CreateController(textBox);

                var anchor = controller.CaptureAnchor();

                Assert.NotNull(anchor);
                Assert.Equal(3, anchor!.Value.SourceLine);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CaptureAnchor_CaretOnFirstLine_ReturnsLineOne()
    {
        WpfTestHost.Run(() =>
        {
            var textBox = new TextBox { Text = "first\nsecond" };
            var window = ShowInWindow(textBox);
            try
            {
                textBox.CaretIndex = 0;
                var controller = CreateController(textBox);

                var anchor = controller.CaptureAnchor();

                Assert.NotNull(anchor);
                Assert.Equal(1, anchor!.Value.SourceLine);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static PreviewViewportController CreateController(TextBox textBox) =>
        new(new FlowDocumentScrollViewer(), textBox, () => EditorTheme.Light, _ => { });

    private static Window ShowInWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 320, Height = 240 };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
