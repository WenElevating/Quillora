using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WpfMarkdownEditor.Wpf.Theming;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Centers the rendered document in a readable column by adjusting the FlowDocument's
/// page padding. The viewer keeps the full pane width so its scrollbar stays at the edge.
/// </summary>
internal sealed class PreviewPageLayoutController : IDisposable
{
    private const double MinimumSidePadding = 24;
    private const double WideBreakpoint = 1400;
    private const double ExtraWideBreakpoint = 1800;
    private const double WideContentWidth = 1024;
    private const double ExtraWideContentWidth = 1200;

    private readonly FlowDocumentScrollViewer _viewer;
    private readonly Func<EditorTheme> _themeProvider;
    private readonly DependencyPropertyDescriptor _zoomDescriptor;

    public PreviewPageLayoutController(FlowDocumentScrollViewer viewer, Func<EditorTheme> themeProvider)
    {
        _viewer = viewer;
        _themeProvider = themeProvider;
        _viewer.SizeChanged += OnViewerSizeChanged;
        _zoomDescriptor = DependencyPropertyDescriptor.FromProperty(
            FlowDocumentScrollViewer.ZoomProperty,
            typeof(FlowDocumentScrollViewer));
        _zoomDescriptor.AddValueChanged(_viewer, OnZoomChanged);
    }

    public void Apply()
    {
        var document = _viewer.Document;
        if (document is null)
            return;

        var available = _viewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
        if (available <= 0)
            return;

        var theme = _themeProvider();
        var targetWidth = ResolveTargetWidth(theme.ContentMaxWidth, available);
        var side = Math.Max(MinimumSidePadding, (available - targetWidth) / 2);
        var padding = new Thickness(side, theme.PagePadding.Top, side, theme.PagePadding.Bottom);
        if (document.PagePadding != padding)
            document.PagePadding = padding;
    }

    public void Dispose()
    {
        _viewer.SizeChanged -= OnViewerSizeChanged;
        _zoomDescriptor.RemoveValueChanged(_viewer, OnZoomChanged);
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e) => Apply();

    private void OnZoomChanged(object? sender, EventArgs e) => Apply();

    private static double ResolveTargetWidth(double baseWidth, double available)
    {
        if (available >= ExtraWideBreakpoint)
            return Math.Max(baseWidth, ExtraWideContentWidth);

        if (available >= WideBreakpoint)
            return Math.Max(baseWidth, WideContentWidth);

        return baseWidth;
    }
}
