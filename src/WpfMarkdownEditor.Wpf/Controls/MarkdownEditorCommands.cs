using System.Windows.Input;

namespace WpfMarkdownEditor.Wpf.Controls;

public static class MarkdownEditorCommands
{
    public static readonly RoutedUICommand PasteImage = new(
        "Paste Image",
        nameof(PasteImage),
        typeof(MarkdownEditorCommands));

    public static readonly RoutedUICommand CopyPlainText = new(
        "Copy as Plain Text",
        nameof(CopyPlainText),
        typeof(MarkdownEditorCommands));

    public static readonly RoutedUICommand PastePlainText = new(
        "Paste as Plain Text",
        nameof(PastePlainText),
        typeof(MarkdownEditorCommands),
        new InputGestureCollection
        {
            new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift),
        });

    public static readonly RoutedUICommand MoveLineUp = new(
        "Move Line Up",
        nameof(MoveLineUp),
        typeof(MarkdownEditorCommands),
        new InputGestureCollection
        {
            new KeyGesture(Key.Up, ModifierKeys.Alt),
        });

    public static readonly RoutedUICommand MoveLineDown = new(
        "Move Line Down",
        nameof(MoveLineDown),
        typeof(MarkdownEditorCommands),
        new InputGestureCollection
        {
            new KeyGesture(Key.Down, ModifierKeys.Alt),
        });

    public static readonly RoutedUICommand DeleteSelectionOrCurrentLine = new(
        "Delete",
        nameof(DeleteSelectionOrCurrentLine),
        typeof(MarkdownEditorCommands));

    public static readonly RoutedUICommand InsertHardLineBreak = new(
        "Insert Hard Line Break",
        nameof(InsertHardLineBreak),
        typeof(MarkdownEditorCommands));

    public static readonly RoutedUICommand ToggleIntegratedMode = new(
        "Toggle Integrated Mode",
        nameof(ToggleIntegratedMode),
        typeof(MarkdownEditorCommands),
        new InputGestureCollection
        {
            new KeyGesture(Key.I, ModifierKeys.Control | ModifierKeys.Shift),
        });
}
