namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Zoom handling for the preview pane.
/// </summary>
internal static class EditorZoom
{
    public static void ZoomOut(MarkdownEditor editor)
    {
        editor.PreviewViewer.DecreaseZoom();
        Sync(editor);
    }

    public static void ZoomIn(MarkdownEditor editor)
    {
        editor.PreviewViewer.IncreaseZoom();
        Sync(editor);
    }

    public static void Reset(MarkdownEditor editor)
    {
        editor.PreviewViewer.Zoom = 100;
        Sync(editor);
    }

    public static void SetZoom(MarkdownEditor editor, double zoom)
    {
        editor.PreviewViewer.Zoom = zoom;
        editor.ZoomText.Text = $"{(int)zoom}%";
    }

    private static void Sync(MarkdownEditor editor)
    {
        var zoom = editor.PreviewViewer.Zoom;
        editor.ZoomSlider.Value = zoom;
        editor.ZoomText.Text = $"{(int)zoom}%";
    }
}
