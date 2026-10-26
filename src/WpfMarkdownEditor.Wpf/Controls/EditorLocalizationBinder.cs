using System.Windows.Automation;
using WpfMarkdownEditor.Wpf.Localization;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Applies localized menu text, tooltips and automation names to the editor chrome.
/// </summary>
internal static class EditorLocalizationBinder
{
    public static void Apply(MarkdownEditor editor, IStringLocalizer localizer)
    {
        editor.UndoMenuItem.Header = localizer.GetString("Editor.Undo");
        editor.RedoMenuItem.Header = localizer.GetString("Editor.Redo");
        editor.CutMenuItem.Header = localizer.GetString("Editor.Cut");
        editor.CopyMenuItem.Header = localizer.GetString("Editor.Copy");
        editor.PasteMenuItem.Header = localizer.GetString("Editor.Paste");
        editor.SelectAllMenuItem.Header = localizer.GetString("Editor.SelectAll");
        editor.ZoomOutBtn.ToolTip = localizer.GetString("Editor.ZoomOut");
        editor.ZoomInBtn.ToolTip = localizer.GetString("Editor.ZoomIn");
        editor.ZoomResetBtn.ToolTip = localizer.GetString("Editor.ResetZoom");

        AutomationProperties.SetName(editor, localizer.GetString("Editor.MarkdownEditor"));
        AutomationProperties.SetName(editor.EditorTextBox, localizer.GetString("Editor.MarkdownSource"));
        AutomationProperties.SetName(editor.EditorSplitter, localizer.GetString("Editor.Splitter"));
        AutomationProperties.SetName(editor.PreviewViewer, localizer.GetString("Editor.Preview"));
        AutomationProperties.SetName(editor.ZoomOutBtn, localizer.GetString("Editor.ZoomOut"));
        AutomationProperties.SetName(editor.ZoomSlider, localizer.GetString("Editor.ZoomLevel"));
        AutomationProperties.SetName(editor.ZoomText, localizer.GetString("Editor.ZoomLevel"));
        AutomationProperties.SetName(editor.ZoomInBtn, localizer.GetString("Editor.ZoomIn"));
        AutomationProperties.SetName(editor.ZoomResetBtn, localizer.GetString("Editor.ResetZoom"));
    }
}
