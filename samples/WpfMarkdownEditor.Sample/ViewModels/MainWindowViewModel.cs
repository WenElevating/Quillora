using System.IO;
using WpfMarkdownEditor.Wpf.Localization;

namespace WpfMarkdownEditor.Sample.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private string? _currentFilePath;
    private bool _isDirty;
    private bool _isLoadingFile;
    private string _currentThemeName = "GitHub";
    private bool _isSidebarOpen;
    private bool _isReadingMode;
    private string _statusKey = "Status.Ready";
    private object[] _statusArgs = [];
    private string _statusText;
    private string _title;

    public MainWindowViewModel(LocalizationService localizationService)
    {
        _localizationService = localizationService;
        _statusText = _localizationService.GetString(_statusKey);
        _title = BuildTitle();
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (SetProperty(ref _currentFilePath, value))
            {
                OnPropertyChanged(nameof(HasCurrentFile));
                RefreshTitle();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                RefreshTitle();
        }
    }

    public bool IsLoadingFile
    {
        get => _isLoadingFile;
        set => SetProperty(ref _isLoadingFile, value);
    }

    public bool HasCurrentFile => CurrentFilePath is not null;

    public string CurrentThemeName
    {
        get => _currentThemeName;
        set => SetProperty(ref _currentThemeName, value);
    }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set => SetProperty(ref _isSidebarOpen, value);
    }

    /// <summary>Reading mode hides the markdown source pane and shows the preview alone.</summary>
    public bool IsReadingMode
    {
        get => _isReadingMode;
        set => SetProperty(ref _isReadingMode, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public void NewDocument()
    {
        CurrentFilePath = null;
        IsDirty = false;
    }

    public void SetCurrentFile(string path)
    {
        CurrentFilePath = path;
        IsDirty = false;
    }

    public void ClearCurrentFile()
    {
        CurrentFilePath = null;
        IsDirty = false;
    }

    public void MarkDirty()
    {
        if (!IsLoadingFile)
            IsDirty = true;
    }

    public void MarkSaved(string path)
    {
        CurrentFilePath = path;
        IsDirty = false;
    }

    public void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        RefreshStatusText();
    }

    public void RefreshLocalizedText()
    {
        RefreshTitle();
        RefreshStatusText();
    }

    private void RefreshTitle() => Title = BuildTitle();

    private string BuildTitle()
    {
        var titleSuffix = _localizationService.GetString("MainWindow.TitleSuffix");
        if (CurrentFilePath is not null)
        {
            var dirtyMarker = IsDirty ? " *" : string.Empty;
            return $"{Path.GetFileName(CurrentFilePath)}{dirtyMarker} - {titleSuffix}";
        }

        return IsDirty ? $"* - {titleSuffix}" : titleSuffix;
    }

    private void RefreshStatusText()
    {
        StatusText = _statusArgs.Length == 0
            ? _localizationService.GetString(_statusKey)
            : _localizationService.Format(_statusKey, _statusArgs);
    }
}
