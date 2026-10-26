using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfMarkdownEditor.Sample.Services;
using WpfMarkdownEditor.Sample.ViewModels;
using WpfMarkdownEditor.Wpf.Controls;
using WpfMarkdownEditor.Wpf.Dialogs;
using WpfMarkdownEditor.Wpf.Localization;
using WpfMarkdownEditor.Wpf.Services;

namespace WpfMarkdownEditor.Sample;

public partial class MainWindow : Window
{
    private const double SidebarWidth = 260;
    private const int SidebarAnimMs = 200;

    private readonly LocalizationService _localizationService;
    private readonly LocalizationSettingsService _localizationSettingsService;
    private readonly RecentFilesService _recentFilesService;
    private readonly Func<MainWindow> _createMainWindow;
    private readonly FileOperationService _fileOperationService;
    private readonly HtmlExportService _htmlExportService;
    private readonly QuickOpenService _quickOpenService;
    private readonly DocumentSessionService _documentSessionService;
    private readonly WorkspaceViewModel _workspaceViewModel;
    private readonly RecentFilesMenuViewModel _recentFilesMenuViewModel;
    private readonly TranslationCoordinator _translationCoordinator;
    private readonly MainWindowViewModel _viewModel;
    private readonly CancellationTokenSource _windowLifetimeCts = new();
    private TranslationProgressOverlay? _progressOverlay;
    private bool _isClosed;

    public MainWindow(
        LocalizationService localizationService,
        LocalizationSettingsService localizationSettingsService,
        RecentFilesService recentFilesService,
        Func<MainWindow> createMainWindow,
        FileOperationService fileOperationService,
        HtmlExportService htmlExportService,
        QuickOpenService quickOpenService,
        DocumentSessionService documentSessionService,
        WorkspaceViewModel workspaceViewModel,
        RecentFilesMenuViewModel recentFilesMenuViewModel,
        TranslationCoordinator translationCoordinator,
        MainWindowViewModel viewModel)
    {
        _localizationService = localizationService;
        _localizationSettingsService = localizationSettingsService;
        _recentFilesService = recentFilesService;
        _createMainWindow = createMainWindow;
        _fileOperationService = fileOperationService;
        _htmlExportService = htmlExportService;
        _quickOpenService = quickOpenService;
        _documentSessionService = documentSessionService;
        _workspaceViewModel = workspaceViewModel;
        _recentFilesMenuViewModel = recentFilesMenuViewModel;
        _translationCoordinator = translationCoordinator;
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = _viewModel;
        WeakEventManager<LocalizationService, LanguageChangedEventArgs>.AddHandler(
            _localizationService,
            nameof(LocalizationService.LanguageChanged),
            OnLanguageChanged);

        Editor.SetLocalizer(_localizationService);
        BuildThemeList();
        BuildLanguageList();
        ApplyTheme("GitHub");
        SetStatus("Status.Ready");
        _ = RefreshRecentFilesCacheAsync();

        // Reposition search panel when editor layout changes
        Editor.TextBox.SizeChanged += (_, _) =>
        {
            if (SearchPanel.Visibility == Visibility.Visible)
                UpdateSearchPanelPosition();
        };

        Editor.MarkdownChanged += OnMarkdownChanged;
        Editor.PreviewRendered += OnPreviewRendered;

        _viewModel.ClearCurrentFile();
        UpdateTitle();
    }

    public void OpenStartupFile(string? filePath)
    {
        if (filePath != null && File.Exists(filePath))
            OpenFilePath(filePath, confirmIfDirty: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _windowLifetimeCts.Cancel();
        RemoveProgressOverlay();
        WeakEventManager<LocalizationService, LanguageChangedEventArgs>.RemoveHandler(
            _localizationService,
            nameof(LocalizationService.LanguageChanged),
            OnLanguageChanged);
        _windowLifetimeCts.Dispose();
        base.OnClosed(e);
    }

    #region File Operations

    private void OnNewFile(object sender, RoutedEventArgs e) => NewFile();

    private void OnOpenFile(object sender, RoutedEventArgs e) => OpenFile();

    private async void OnSaveFile(object sender, RoutedEventArgs e) => await SaveCurrentFileAsync();

    private void OnNewWindow(object sender, RoutedEventArgs e) => NewWindow();
    private void OnOpenFolder(object sender, RoutedEventArgs e) => OpenFolder();
    private void OnQuickOpen(object sender, RoutedEventArgs e) => QuickOpen();
    private void OnOpenRecentFile(object sender, RoutedEventArgs e) => OpenRecentFileMenu(e);
    private async void OnOpenRecentFileMouseEnter(object sender, MouseEventArgs e) => await OpenRecentFileMenuAfterHoverDelayAsync();
    private void OnOpenRecentFileMouseLeave(object sender, MouseEventArgs e)
    {
        CancelRecentFilesHover();
        ScheduleRecentFilesMenuClose();
    }

    private void OnRecentFilesMenuMouseEnter(object sender, MouseEventArgs e) => CancelRecentFilesMenuClose();
    private void OnRecentFilesMenuMouseLeave(object sender, MouseEventArgs e) => ScheduleRecentFilesMenuClose();
    private async void OnSaveFileAs(object sender, RoutedEventArgs e) => await SaveCurrentFileAsAsync();
    private void OnMoveFile(object sender, RoutedEventArgs e) => MoveCurrentFile();
    private void OnShowFileProperties(object sender, RoutedEventArgs e) => ShowCurrentFileProperties();
    private void OnOpenFileLocation(object sender, RoutedEventArgs e) => OpenCurrentFileLocation();
    private async void OnShowInSidebar(object sender, RoutedEventArgs e) => await ShowCurrentFileInSidebarAsync();
    private void OnDeleteFile(object sender, RoutedEventArgs e) => DeleteCurrentFile();
    private async void OnImportFile(object sender, RoutedEventArgs e) => await ImportFileIntoDocumentAsync();
    private void OnExportHtml(object sender, RoutedEventArgs e) => ExportCurrentDocumentAsHtml();
    private void OnPrintFile(object sender, RoutedEventArgs e) => PrintCurrentDocument();
    private void OnPreferences(object sender, RoutedEventArgs e) => OpenPreferences();
    private void OnCloseWindow(object sender, RoutedEventArgs e) => CloseCurrentWindow();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N: e.Handled = true; NewFile(); break;
                case Key.O: e.Handled = true; OpenFile(); break;
                case Key.S: e.Handled = true; _ = SaveCurrentFileAsync(); break;
                case Key.F: e.Handled = true; ShowSearchPanel(); break;
                case Key.P: e.Handled = true; QuickOpen(); break;
                case Key.W: e.Handled = true; CloseCurrentWindow(); break;
                case Key.OemComma: e.Handled = true; OpenPreferences(); break;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.N: e.Handled = true; NewWindow(); break;
                case Key.S: e.Handled = true; _ = SaveCurrentFileAsAsync(); break;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && e.Key == Key.P)
        {
            e.Handled = true;
            PrintCurrentDocument();
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var fileName = _viewModel.CurrentFilePath != null
                ? System.IO.Path.GetFileName(_viewModel.CurrentFilePath)
                : _localizationService.GetString("MainWindow.Untitled");
            var result = ShowSaveConfirmation(fileName);

            switch (result)
            {
                case SaveConfirmationResult.Save:
                    if (!SaveCurrentFileSync()) e.Cancel = true;
                    break;
                case SaveConfirmationResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }
        base.OnClosing(e);
    }

    private void NewFile()
    {
        if (!ConfirmSaveIfDirty()) return;
        _viewModel.IsLoadingFile = true;
        Editor.Markdown = string.Empty;
        Editor.DocumentPath = null;
        _viewModel.IsLoadingFile = false;
        _viewModel.NewDocument();
        UpdateTitle();
        SetStatus("Status.NewFile");
    }

    private void NewWindow()
    {
        var window = _createMainWindow();
        window.Show();
    }

    private void OpenFile()
    {
        if (!ConfirmSaveIfDirty()) return;

        var dialog = new OpenFileDialog
        {
            Filter = _localizationService.GetString("FileDialog.MarkdownFilter"),
            DefaultExt = ".md"
        };
        if (dialog.ShowDialog() != true) return;

        OpenFilePath(dialog.FileName, confirmIfDirty: false);
    }

    private bool OpenFilePath(string path, bool confirmIfDirty = true)
    {
        if (confirmIfDirty && !ConfirmSaveIfDirty()) return false;

        try
        {
            _viewModel.IsLoadingFile = true;
            Editor.LoadFile(path);
            _viewModel.IsLoadingFile = false;
            _viewModel.SetCurrentFile(path);
            _recentFilesMenuViewModel.AddOrRefresh(path);
            _ = LoadCurrentFileDirectoryTreeAsync(path);
            UpdateTitle();
            SetStatus("Status.FileLoaded", path);
            return true;
        }
        catch (Exception ex)
        {
            _viewModel.IsLoadingFile = false;
            if (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _recentFilesService.RemoveFile(path);
                _recentFilesMenuViewModel.RemoveFromCache(path);
            }

            MessageBox.Show(_localizationService.Format("Error.LoadFile", ex.Message), _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<bool> SaveCurrentFileAsync()
    {
        string? targetPath = _viewModel.CurrentFilePath;

        if (targetPath == null)
            return await SaveCurrentFileAsAsync();

        try
        {
            await Editor.SaveFileAsync(targetPath);
            Editor.DocumentPath = targetPath;
            _viewModel.MarkSaved(targetPath);
            _recentFilesMenuViewModel.AddOrRefresh(targetPath);
            _ = LoadCurrentFileDirectoryTreeAsync(targetPath);
            UpdateTitle();
            SetStatus("Status.FileSaved", targetPath);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(_localizationService.Format("Error.SaveFile", ex.Message), _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<bool> SaveCurrentFileAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.GetString("FileDialog.MarkdownFilter"),
            DefaultExt = ".md",
            FileName = _viewModel.CurrentFilePath != null ? System.IO.Path.GetFileName(_viewModel.CurrentFilePath) : string.Empty,
        };
        if (dialog.ShowDialog() != true) return false;

        try
        {
            await Editor.SaveFileAsync(dialog.FileName);
            Editor.DocumentPath = dialog.FileName;
            _viewModel.MarkSaved(dialog.FileName);
            _recentFilesMenuViewModel.AddOrRefresh(dialog.FileName);
            _ = LoadCurrentFileDirectoryTreeAsync(dialog.FileName);
            UpdateTitle();
            SetStatus("Status.FileSaved", dialog.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(_localizationService.Format("Error.SaveFile", ex.Message), _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async void OpenFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = _localizationService.GetString("MainWindow.OpenFolder")
        };
        if (dialog.ShowDialog() != true)
            return;

        if (await LoadWorkspaceFolderAsync(dialog.FolderName))
        {
            ShowFilesTab();
            OpenSidebar();
        }
    }

    private async Task<bool> LoadWorkspaceFolderAsync(string folderPath)
    {
        try
        {
            var result = await _workspaceViewModel.LoadFolderAsync(folderPath);
            if (result is null)
                return false;

            FilesTree.ItemsSource = _workspaceViewModel.RootChildren;
            UpdateFilesPanelState();

            if (result.IsTruncated)
                SetStatus("Status.FolderScanTruncated", result.MarkdownFileCount);
            else
                SetStatus("Status.Ready");

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<WorkspaceTreeNode?> LoadCurrentFileDirectoryTreeAsync(string filePath)
    {
        try
        {
            var node = await _workspaceViewModel.LoadCurrentFileDirectoryAsync(filePath);
            if (node is null)
                return null;

            FilesTree.ItemsSource = _workspaceViewModel.RootChildren;
            UpdateFilesPanelState();
            return node;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task LoadWorkspaceNodeChildrenAsync(WorkspaceTreeNode node)
    {
        await _workspaceViewModel.LoadNodeChildrenAsync(node);
        UpdateFilesPanelState();
    }

    private void QuickOpen()
    {
        var items = _quickOpenService.BuildItems(
            _recentFilesService.LoadFiles(removeMissingFiles: true),
            _workspaceViewModel.Root);
        ShowQuickOpenDialog(items);
    }

    private void ShowQuickOpenDialog(IReadOnlyList<QuickOpenItem> items)
    {
        var dialog = new QuickOpenDialog(items) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedItem is not null)
            OpenFilePath(dialog.SelectedItem.Path);

        Editor.Focus();
    }

    private void MoveCurrentFile()
    {
        if (_viewModel.CurrentFilePath is null)
            return;

        if (_viewModel.IsDirty && !SaveCurrentFileSync())
            return;

        var dialog = new SaveFileDialog
        {
            FileName = System.IO.Path.GetFileName(_viewModel.CurrentFilePath),
            Filter = _localizationService.GetString("FileDialog.MarkdownFilter"),
            DefaultExt = ".md"
        };
        if (dialog.ShowDialog() != true)
            return;

        var oldFullPath = System.IO.Path.GetFullPath(_viewModel.CurrentFilePath);
        var newFullPath = System.IO.Path.GetFullPath(dialog.FileName);
        if (string.Equals(oldFullPath, newFullPath, StringComparison.OrdinalIgnoreCase))
            return;

        var overwrite = false;
        if (File.Exists(newFullPath))
        {
            overwrite = MessageBox.Show(
                _localizationService.GetString("MainWindow.OverwriteFilePrompt"),
                _localizationService.GetString("Common.Validation"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!overwrite)
                return;
        }

        try
        {
            var oldPath = _viewModel.CurrentFilePath;
            _fileOperationService.MoveFile(oldPath, dialog.FileName, overwrite);
            _recentFilesService.RemoveFile(oldPath);
            _recentFilesMenuViewModel.RemoveFromCache(oldPath);
            _recentFilesMenuViewModel.AddOrRefresh(dialog.FileName);
            RemoveWorkspaceNode(oldPath);
            Editor.DocumentPath = dialog.FileName;
            _viewModel.MarkSaved(dialog.FileName);
            _ = LoadCurrentFileDirectoryTreeAsync(dialog.FileName);
            UpdateTitle();
            SetStatus("Status.FileSaved", dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowCurrentFileProperties()
    {
        if (_viewModel.CurrentFilePath is null)
            return;

        try
        {
            var properties = _fileOperationService.GetProperties(_viewModel.CurrentFilePath);
            MessageBox.Show(
                $"{properties.Path}{Environment.NewLine}{properties.SizeBytes:N0} bytes{Environment.NewLine}{properties.LastModifiedUtc:u}",
                _localizationService.GetString("MainWindow.Properties"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenCurrentFileLocation()
    {
        if (_viewModel.CurrentFilePath is null)
            return;

        try
        {
            _fileOperationService.OpenFileLocation(_viewModel.CurrentFilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ShowCurrentFileInSidebarAsync()
    {
        if (_viewModel.CurrentFilePath is null)
            return;

        var node = await LoadCurrentFileDirectoryTreeAsync(_viewModel.CurrentFilePath);
        if (node is null)
            return;

        ShowFilesTab();
        OpenSidebar();
        BringWorkspaceNodeIntoView(node);
    }

    private void BringWorkspaceNodeIntoView(WorkspaceTreeNode node)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var item = FindTreeViewItem(FilesTree, node);
            item?.BringIntoView();
            item?.Focus();
        }, DispatcherPriority.Background);
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl container, WorkspaceTreeNode target)
    {
        container.UpdateLayout();

        foreach (var item in container.Items)
        {
            if (container.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem treeViewItem)
                continue;

            if (ReferenceEquals(item, target))
                return treeViewItem;

            var childItem = FindTreeViewItem(treeViewItem, target);
            if (childItem is not null)
                return childItem;
        }

        return null;
    }

    private void DeleteCurrentFile()
    {
        if (_viewModel.CurrentFilePath is null)
            return;

        var confirm = MessageBox.Show(
            _localizationService.GetString("MainWindow.DeleteFilePrompt"),
            _localizationService.GetString("MainWindow.DeleteFile"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var deletedPath = _viewModel.CurrentFilePath;
            _fileOperationService.DeleteFile(deletedPath);
            _recentFilesService.RemoveFile(deletedPath);
            _recentFilesMenuViewModel.RemoveFromCache(deletedPath);
            RemoveWorkspaceNode(deletedPath);
            _viewModel.IsLoadingFile = true;
            Editor.Markdown = string.Empty;
            Editor.DocumentPath = null;
            _viewModel.IsLoadingFile = false;
            _viewModel.ClearCurrentFile();
            UpdateTitle();
            SetStatus("Status.NewFile");
        }
        catch (Exception ex)
        {
            _viewModel.IsLoadingFile = false;
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ImportFileIntoDocumentAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = _localizationService.GetString("FileDialog.ImportFilter"),
            DefaultExt = ".md"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var markdown = await _documentSessionService.ReadMarkdownAsync(dialog.FileName);
            Editor.AppendMarkdown(markdown);
            _viewModel.MarkDirty();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportCurrentDocumentAsHtml()
    {
        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.GetString("FileDialog.HtmlFilter"),
            DefaultExt = ".html"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var title = _viewModel.CurrentFilePath is null
                ? _localizationService.GetString("MainWindow.Untitled")
                : System.IO.Path.GetFileNameWithoutExtension(_viewModel.CurrentFilePath);
            _htmlExportService.ExportHtmlToFile(Editor.Markdown, title, dialog.FileName);
            SetStatus("Status.FileSaved", dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _localizationService.Format("Error.FileOperation", ex.Message),
                _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PrintCurrentDocument()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
            return;

        var document = Editor.TryGetPrintablePreviewDocument(out var previewDocument)
            ? previewDocument
            : Editor.CreatePlainTextPrintDocument();
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, Title);
    }

    private void OpenPreferences()
    {
        var dialog = new PreferencesDialog(_localizationService, _localizationService.CurrentLanguage, _viewModel.CurrentThemeName)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        _localizationService.SetLanguage(dialog.SelectedLanguage);
        _localizationSettingsService.SaveLanguage(dialog.SelectedLanguage);
        ApplyTheme(dialog.SelectedThemeName);

        if (dialog.OpenTranslationSettingsRequested)
            OpenTranslationSettings();
    }

    private void CloseCurrentWindow() => Close();

    private void RemoveWorkspaceNode(string path)
    {
        _workspaceViewModel.RemoveNode(path);
    }

    private bool SaveCurrentFileSync()
    {
        string? targetPath = _viewModel.CurrentFilePath;

        if (targetPath == null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = _localizationService.GetString("FileDialog.MarkdownFilter"),
                DefaultExt = ".md"
            };
            if (dialog.ShowDialog() != true) return false;
            targetPath = dialog.FileName;
        }

        try
        {
            // WPF closing cancellation is synchronous; async save cannot be awaited from this path.
            _documentSessionService.WriteMarkdown(targetPath, Editor.Markdown);
            Editor.DocumentPath = targetPath;
            _viewModel.MarkSaved(targetPath);
            _recentFilesMenuViewModel.AddOrRefresh(targetPath);
            _ = LoadCurrentFileDirectoryTreeAsync(targetPath);
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(_localizationService.Format("Error.SaveFile", ex.Message), _localizationService.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool ConfirmSaveIfDirty()
    {
        if (!_viewModel.IsDirty) return true;

        var fileName = _viewModel.CurrentFilePath != null
            ? System.IO.Path.GetFileName(_viewModel.CurrentFilePath)
            : _localizationService.GetString("MainWindow.Untitled");
        var result = ShowSaveConfirmation(fileName);

        return result switch
        {
            SaveConfirmationResult.Save => SaveCurrentFileSync(),
            SaveConfirmationResult.DontSave => true,
            _ => false
        };
    }

    private SaveConfirmationResult ShowSaveConfirmation(string fileName)
    {
        var dialog = new SaveConfirmationDialog(fileName, _localizationService) { Owner = this };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void UpdateTitle()
    {
        Title = _viewModel.Title;
        UpdateFileScopedMenuItems();
    }

    private void UpdateFileScopedMenuItems()
    {
        var hasCurrentFile = _viewModel.HasCurrentFile;
        FilePropertiesButton.IsEnabled = hasCurrentFile;
        OpenFileLocationButton.IsEnabled = hasCurrentFile;
        ShowInSidebarButton.IsEnabled = hasCurrentFile;
        DeleteFileButton.IsEnabled = hasCurrentFile;
    }

    #endregion
}
