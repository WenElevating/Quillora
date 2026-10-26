using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Theming;
using WpfMarkdownEditor.Wpf.Localization;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Embeddable Markdown editor with side-by-side preview.
/// </summary>
public partial class MarkdownEditor : UserControl, IDisposable
{
    private readonly PreviewViewportController _viewportController;
    private readonly PreviewRenderController _previewController;
    private readonly ImagePasteService _imagePasteService;
    private readonly SmartEditingController _smartEditing;
    private LocalizationService? _localizationService;

    #region DependencyProperties

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownEditor),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkdownChanged));

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(EditorTheme),
            typeof(MarkdownEditor),
            new PropertyMetadata(EditorTheme.Light, OnThemeChanged));

    public static readonly DependencyProperty ShowPreviewProperty =
        DependencyProperty.Register(
            nameof(ShowPreview),
            typeof(bool),
            typeof(MarkdownEditor),
            new PropertyMetadata(true));

    public static readonly DependencyProperty PreviewWidthProperty =
        DependencyProperty.Register(
            nameof(PreviewWidth),
            typeof(GridLength),
            typeof(MarkdownEditor),
            new PropertyMetadata(new GridLength(1, GridUnitType.Star)));

    public static readonly DependencyProperty DocumentPathProperty =
        DependencyProperty.Register(
            nameof(DocumentPath),
            typeof(string),
            typeof(MarkdownEditor),
            new PropertyMetadata(null, OnDocumentPathChanged));

    public static readonly DependencyProperty EnableScrollSyncProperty =
        DependencyProperty.Register(
            nameof(EnableScrollSync),
            typeof(bool),
            typeof(MarkdownEditor),
            new PropertyMetadata(true, OnViewportOptionsChanged));

    public static readonly DependencyProperty HighlightActiveBlockProperty =
        DependencyProperty.Register(
            nameof(HighlightActiveBlock),
            typeof(bool),
            typeof(MarkdownEditor),
            new PropertyMetadata(true, OnViewportOptionsChanged));

    #endregion

    #region Public API

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public EditorTheme Theme
    {
        get => (EditorTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public bool ShowPreview
    {
        get => (bool)GetValue(ShowPreviewProperty);
        set => SetValue(ShowPreviewProperty, value);
    }

    public GridLength PreviewWidth
    {
        get => (GridLength)GetValue(PreviewWidthProperty);
        set => SetValue(PreviewWidthProperty, value);
    }

    public string? DocumentPath
    {
        get => (string?)GetValue(DocumentPathProperty);
        set => SetValue(DocumentPathProperty, value);
    }

    /// <summary>
    /// When true, the preview scrolls to the block containing the source caret line.
    /// </summary>
    public bool EnableScrollSync
    {
        get => (bool)GetValue(EnableScrollSyncProperty);
        set => SetValue(EnableScrollSyncProperty, value);
    }

    /// <summary>
    /// When true, the preview block containing the source caret line is highlighted.
    /// </summary>
    public bool HighlightActiveBlock
    {
        get => (bool)GetValue(HighlightActiveBlockProperty);
        set => SetValue(HighlightActiveBlockProperty, value);
    }

    public event EventHandler<MarkdownChangedEventArgs>? MarkdownChanged;

    /// <summary>
    /// Raised on the UI thread after a preview render completes. The event carries the
    /// parsed AST so consumers can reuse it instead of re-parsing the Markdown.
    /// </summary>
    public event EventHandler<PreviewRenderedEventArgs>? PreviewRendered;

    #endregion

    public MarkdownEditor()
    {
        InitializeComponent();
        _viewportController = new PreviewViewportController(
            PreviewViewer,
            EditorTextBox,
            () => Theme,
            GoToLine)
        {
            EnableScrollSync = EnableScrollSync,
            HighlightActiveBlock = HighlightActiveBlock,
        };
        _previewController = new PreviewRenderController(
            PreviewViewer,
            () => Markdown,
            () => Theme,
            GetDocumentBaseDirectory,
            _viewportController,
            args => PreviewRendered?.Invoke(this, args));
        _imagePasteService = new ImagePasteService(GetDocumentBaseDirectory);
        _smartEditing = new SmartEditingController(EditorTextBox, ApplyTextOperation);

        // Bind standard editing commands to the TextBox
        EditorTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo));
        EditorTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo));
        EditorTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut));
        EditorTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy));
        EditorTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPasteExecuted));
        EditorTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, OnSelectAllExecuted));

        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.PasteImage, OnPasteImageExecuted, OnCanPasteImage));
        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.CopyPlainText, OnCopyPlainTextExecuted, OnCanCopyPlainText));
        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.PastePlainText, OnPastePlainTextExecuted, OnCanPastePlainText));
        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.MoveLineUp, OnMoveLineUpExecuted, OnCanMoveLineUp));
        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.MoveLineDown, OnMoveLineDownExecuted, OnCanMoveLineDown));
        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.DeleteSelectionOrCurrentLine, OnDeleteSelectionOrCurrentLineExecuted, OnCanDeleteSelectionOrCurrentLine));
        CommandBindings.Add(new CommandBinding(MarkdownEditorCommands.InsertHardLineBreak, OnInsertHardLineBreakExecuted));

        EditorLocalizationBinder.Apply(this, FallbackStringLocalizer.Instance);
    }

    #region Methods

    public void LoadFile(string path)
    {
        var markdown = File.ReadAllText(path);
        DocumentPath = path;
        Markdown = markdown;
    }

    public async Task SaveFileAsync(string path)
    {
        var content = Markdown;
        await Task.Run(() => File.WriteAllText(path, content));
    }

    public void ApplyTheme(EditorTheme theme) => Theme = theme;

    public void FocusEditor() => EditorTextBox.Focus();

    /// <summary>
    /// Places the caret on the requested 1-based source line and scrolls it into view.
    /// </summary>
    public void GoToLine(int line)
    {
        if (line < 1) line = 1;

        var lineCount = EditorTextBox.LineCount;
        if (lineCount > 0 && line > lineCount)
            line = lineCount;

        var index = EditorTextBox.GetCharacterIndexFromLineIndex(line - 1);
        if (index < 0)
            index = EditorTextBox.Text.Length;

        EditorTextBox.Select(index, 0);
        EditorTextBox.ScrollToLine(line - 1);
        EditorTextBox.Focus();
    }

    public TextBox TextBox => EditorTextBox;

    public void AppendMarkdown(string content)
    {
        if (string.IsNullOrEmpty(content))
            return;

        var current = Markdown;
        if (!string.IsNullOrEmpty(current) && !current.EndsWith("\n", StringComparison.Ordinal))
            current += Environment.NewLine + Environment.NewLine;

        var updated = current + content;
        EditorTextBox.Text = updated;
        Markdown = updated;
        EditorTextBox.CaretIndex = Markdown.Length;
        EditorTextBox.Focus();
    }

    public bool TryGetPrintablePreviewDocument(out FlowDocument document)
    {
        document = null!;
        if (!ShowPreview)
            return false;

        return _previewController.TryGetPrintablePreviewDocument(out document);
    }

    public FlowDocument CreatePlainTextPrintDocument()
    {
        var document = new FlowDocument();
        document.Blocks.Add(new Paragraph(new Run(Markdown)));
        return document;
    }

    public void SetLocalizer(LocalizationService localizationService)
    {
        if (_localizationService != null)
        {
            WeakEventManager<LocalizationService, LanguageChangedEventArgs>.RemoveHandler(
                _localizationService,
                nameof(LocalizationService.LanguageChanged),
                OnLanguageChanged);
        }

        _localizationService = localizationService;
        WeakEventManager<LocalizationService, LanguageChangedEventArgs>.AddHandler(
            localizationService,
            nameof(LocalizationService.LanguageChanged),
            OnLanguageChanged);

        EditorLocalizationBinder.Apply(this, localizationService);
    }

    /// <summary>
    /// Wrap the selected text with before/after markers, or insert a placeholder.
    /// </summary>
    public void WrapSelection(string before, string after)
    {
        var textBox = EditorTextBox;
        var selectedText = textBox.SelectedText;

        if (string.IsNullOrEmpty(selectedText))
        {
            var placeholder = "text";
            var insertionPos = textBox.SelectionStart;
            textBox.SelectedText = before + placeholder + after;
            textBox.Select(insertionPos + before.Length, placeholder.Length);
        }
        else
        {
            var selectionStart = textBox.SelectionStart;
            textBox.SelectedText = before + selectedText + after;
            textBox.Select(selectionStart + before.Length, selectedText.Length);
        }

        textBox.Focus();
    }

    /// <summary>
    /// Insert text at the cursor position.
    /// </summary>
    public void InsertText(string text)
    {
        var textBox = EditorTextBox;
        textBox.SelectedText = text;
        textBox.Focus();
    }

    /// <summary>
    /// Replace all text as a single undo unit. Used by translation feature.
    /// </summary>
    public void ReplaceAllText(string newText)
    {
        EditorTextBox.BeginChange();
        EditorTextBox.SelectAll();
        EditorTextBox.SelectedText = newText;
        EditorTextBox.EndChange();
    }

    /// <summary>
    /// Render translated markdown in the preview pane without changing editor text.
    /// </summary>
    public void RenderTranslatedPreview(string translatedMarkdown)
    {
        _previewController.RenderTranslatedPreview(translatedMarkdown);
    }

    /// <summary>
    /// Clear translated preview and revert to rendering the editor's content.
    /// </summary>
    public void ClearTranslatedPreview()
    {
        _previewController.ClearTranslatedPreview();
    }

    public void SetHeadingLevel(int level) =>
        ApplyParagraphOperation((text, start, length) =>
            MarkdownParagraphOperations.SetHeadingLevel(text, start, length, level));

    public void ClearParagraphStyle() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.ClearBlockPrefix);

    public void ClearInlineStyle()
    {
        var operation = MarkdownInlineFormatOperations.ClearInlineStyle(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength);
        if (operation is null)
        {
            EditorTextBox.Focus();
            return;
        }

        ApplyTextOperation(operation.Value);
    }

    public void ToggleBlockquote() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.ToggleBlockquote);

    public void ToggleOrderedList() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.ToggleOrderedList);

    public void ToggleBulletList() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.ToggleBulletList);

    public void InsertParagraphAbove() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.InsertParagraphAbove);

    public void InsertParagraphBelow() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.InsertParagraphBelow);

    public void InsertHorizontalRule() =>
        ApplyParagraphOperation(MarkdownParagraphOperations.InsertHorizontalRule);

    /// <summary>
    /// Toggle a line prefix for existing callers. New paragraph menu actions use
    /// the explicit paragraph methods above.
    /// </summary>
    public void ToggleLinePrefix(string prefix)
    {
        switch (prefix)
        {
            case "#":
                SetHeadingLevel(1);
                break;
            case "##":
                SetHeadingLevel(2);
                break;
            case "###":
                SetHeadingLevel(3);
                break;
            case ">":
                ToggleBlockquote();
                break;
            case "1.":
                ToggleOrderedList();
                break;
            case "-":
                ToggleBulletList();
                break;
            default:
                throw new ArgumentException($"Unsupported line prefix: {prefix}", nameof(prefix));
        }
    }

    #endregion

    #region Private Implementation

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (MarkdownEditor)d;
        editor.MarkdownChanged?.Invoke(editor, new MarkdownChangedEventArgs
        {
            OldMarkdown = (string?)e.OldValue ?? string.Empty,
            NewMarkdown = (string?)e.NewValue ?? string.Empty,
        });
        editor._previewController.OnMarkdownChanged();
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (MarkdownEditor)d;
        var theme = (EditorTheme)e.NewValue;
        editor._previewController.OnThemeChanged();
        EditorChromeTheme.Apply(editor, theme);
    }

    private static void OnDocumentPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (MarkdownEditor)d;
        editor._previewController.OnDocumentPathChanged();
    }

    private static void OnViewportOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (MarkdownEditor)d;
        editor._viewportController.EnableScrollSync = editor.EnableScrollSync;
        editor._viewportController.HighlightActiveBlock = editor.HighlightActiveBlock;
        editor._viewportController.Refresh();
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        if (_localizationService is not null)
            EditorLocalizationBinder.Apply(this, _localizationService);
    }

    private string GetDocumentBaseDirectory()
    {
        if (string.IsNullOrWhiteSpace(DocumentPath))
            return AppContext.BaseDirectory;

        try
        {
            var fullPath = System.IO.Path.GetFullPath(DocumentPath);
            return System.IO.Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return AppContext.BaseDirectory;
        }
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        _previewController.OnMarkdownChanged();
    }

    #region Paste Image Path

    private void OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var textBox = EditorTextBox;

        if (TryPasteImage())
        {
            e.Handled = true;
            return;
        }

        // Standard text paste fallback
        if (Clipboard.ContainsText())
        {
            textBox.SelectedText = Clipboard.GetText();
            textBox.Focus();
            e.Handled = true;
        }
    }

    private void OnSelectAllExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        EditorTextBox.SelectAll();
        EditorTextBox.Focus();
        e.Handled = true;
    }

    private void OnCanPasteImage(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _imagePasteService.CanPasteImage();
        e.Handled = true;
    }

    private void OnPasteImageExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = TryPasteImage();
    }

    private void OnCanCopyPlainText(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = EditorTextBox.SelectionLength > 0;
        e.Handled = true;
    }

    private void OnCopyPlainTextExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (EditorTextBox.SelectionLength <= 0)
            return;

        Clipboard.SetText(EditorTextBox.SelectedText, TextDataFormat.UnicodeText);
        e.Handled = true;
    }

    private void OnCanPastePlainText(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = ClipboardContainsUnicodeText();
        e.Handled = true;
    }

    private void OnPastePlainTextExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ClipboardContainsUnicodeText())
            return;

        var operation = EditorTextOperations.InsertText(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength,
            Clipboard.GetText(TextDataFormat.UnicodeText));
        ApplyTextOperation(operation);
        e.Handled = true;
    }

    private void OnCanMoveLineUp(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = EditorTextOperations.MoveSelectedLines(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength,
            -1) is not null;
        e.Handled = true;
    }

    private void OnMoveLineUpExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var operation = EditorTextOperations.MoveSelectedLines(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength,
            -1);
        if (operation is null)
            return;

        ApplyTextOperation(operation.Value);
        e.Handled = true;
    }

    private void OnCanMoveLineDown(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = EditorTextOperations.MoveSelectedLines(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength,
            1) is not null;
        e.Handled = true;
    }

    private void OnMoveLineDownExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var operation = EditorTextOperations.MoveSelectedLines(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength,
            1);
        if (operation is null)
            return;

        ApplyTextOperation(operation.Value);
        e.Handled = true;
    }

    private void OnCanDeleteSelectionOrCurrentLine(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = EditorTextOperations.DeleteSelectionOrCurrentLine(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength) is not null;
        e.Handled = true;
    }

    private void OnDeleteSelectionOrCurrentLineExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var operation = EditorTextOperations.DeleteSelectionOrCurrentLine(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength);
        if (operation is null)
            return;

        ApplyTextOperation(operation.Value);
        e.Handled = true;
    }

    private void OnInsertHardLineBreakExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var operation = EditorTextOperations.InsertText(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength,
            "  " + Environment.NewLine);
        ApplyTextOperation(operation);
        e.Handled = true;
    }

    private void ApplyTextOperation(TextEditOperation operation)
    {
        EditorTextBox.BeginChange();
        try
        {
            EditorTextBox.Text = operation.Text;
            Markdown = operation.Text;
            var selectionStart = Math.Clamp(operation.SelectionStart, 0, EditorTextBox.Text.Length);
            var selectionLength = Math.Clamp(operation.SelectionLength, 0, EditorTextBox.Text.Length - selectionStart);
            EditorTextBox.Select(selectionStart, selectionLength);
        }
        finally
        {
            EditorTextBox.EndChange();
        }

        EditorTextBox.Focus();
    }

    private void ApplyParagraphOperation(Func<string, int, int, TextEditOperation> operationFactory)
    {
        var operation = operationFactory(
            EditorTextBox.Text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength);
        ApplyTextOperation(operation);
    }

    private static bool ClipboardContainsUnicodeText()
    {
        try
        {
            return Clipboard.ContainsText(TextDataFormat.UnicodeText);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }

    private bool TryPasteImage()
    {
        if (!_imagePasteService.TryPasteImage(
                EditorTextBox.Text,
                EditorTextBox.SelectionStart,
                EditorTextBox.SelectionLength,
                ApplyTextOperation))
        {
            return false;
        }

        _previewController.RenderNow();
        return true;
    }

    #endregion

    #region Zoom Controls

    private void OnZoomOut(object sender, RoutedEventArgs e) => EditorZoom.ZoomOut(this);

    private void OnZoomIn(object sender, RoutedEventArgs e) => EditorZoom.ZoomIn(this);

    private void OnZoomReset(object sender, RoutedEventArgs e) => EditorZoom.Reset(this);

    private void OnZoomSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewViewer == null || ZoomText == null) return;
        EditorZoom.SetZoom(this, e.NewValue);
    }

    #endregion

    #region Smart List Editing

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e) =>
        _smartEditing.HandlePreviewKeyDown(e);

    #endregion

    public void Dispose()
    {
        if (_localizationService != null)
        {
            WeakEventManager<LocalizationService, LanguageChangedEventArgs>.RemoveHandler(
                _localizationService,
                nameof(LocalizationService.LanguageChanged),
                OnLanguageChanged);
        }

        _viewportController.Dispose();
        _previewController.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}

public sealed class MarkdownChangedEventArgs : EventArgs
{
    public string OldMarkdown { get; init; } = string.Empty;
    public string NewMarkdown { get; init; } = string.Empty;
}
