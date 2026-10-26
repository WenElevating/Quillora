using System.Windows.Controls;
using System.Windows.Input;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Routes smart Enter/Tab handling from the source TextBox to
/// <see cref="MarkdownEditingOperations"/> and applies the result through the
/// editor's undo-aware text operation callback.
/// </summary>
internal sealed class SmartEditingController(TextBox textBox, Action<TextEditOperation> applyEdit)
{
    public void HandlePreviewKeyDown(KeyEventArgs e)
    {
        // Never intercept keys that the IME is still composing.
        if (e.Key == Key.ImeProcessed)
            return;

        if (e.Key == Key.Enter)
        {
            if (MarkdownEditingOperations.TryHandleEnter(textBox.Text, textBox.CaretIndex, out var operation))
            {
                e.Handled = true;
                Apply(operation);
            }
        }
        else if (e.Key == Key.Tab)
        {
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (MarkdownEditingOperations.TryHandleTab(textBox.Text, textBox.CaretIndex, shift, out var operation))
            {
                e.Handled = true;
                Apply(operation);
            }
        }
    }

    private void Apply(TextEditOperation operation)
    {
        var unchanged = operation.Text == textBox.Text
            && operation.SelectionStart == textBox.SelectionStart
            && operation.SelectionLength == textBox.SelectionLength;
        if (!unchanged)
            applyEdit(operation);
    }
}
