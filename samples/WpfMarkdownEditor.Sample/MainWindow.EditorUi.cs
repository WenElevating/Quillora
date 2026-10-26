using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfMarkdownEditor.Sample.Models;
using WpfMarkdownEditor.Sample.Services;
using WpfMarkdownEditor.Wpf.Controls;

namespace WpfMarkdownEditor.Sample;

public partial class MainWindow
{
    private SearchResultSet _searchResultSet = SearchResultSet.Empty;
    private IReadOnlyList<OutlineEntry> _outlineEntries = [];

    private void OnPopupItemClick(object sender, RoutedEventArgs e)
    {
        RecentFilesPopup.IsOpen = false;
        FilePopup.IsOpen = false;
        EditPopup.IsOpen = false;
        ParagraphPopup.IsOpen = false;
        FormatPopup.IsOpen = false;
        InsertPopup.IsOpen = false;
        ViewPopup.IsOpen = false;
        ToolsPopup.IsOpen = false;
    }

    private void OnHeading1(object sender, RoutedEventArgs e) => Editor.SetHeadingLevel(1);
    private void OnHeading2(object sender, RoutedEventArgs e) => Editor.SetHeadingLevel(2);
    private void OnHeading3(object sender, RoutedEventArgs e) => Editor.SetHeadingLevel(3);
    private void OnHeading4(object sender, RoutedEventArgs e) => Editor.SetHeadingLevel(4);
    private void OnHeading5(object sender, RoutedEventArgs e) => Editor.SetHeadingLevel(5);
    private void OnHeading6(object sender, RoutedEventArgs e) => Editor.SetHeadingLevel(6);
    private void OnParagraphStyle(object sender, RoutedEventArgs e) => Editor.ClearParagraphStyle();
    private void OnBold(object sender, RoutedEventArgs e) => Editor.WrapSelection("**", "**");
    private void OnItalic(object sender, RoutedEventArgs e) => Editor.WrapSelection("*", "*");
    private void OnUnderline(object sender, RoutedEventArgs e) => Editor.WrapSelection("<u>", "</u>");
    private void OnStrikethrough(object sender, RoutedEventArgs e) => Editor.WrapSelection("~~", "~~");
    private void OnInlineCode(object sender, RoutedEventArgs e) => Editor.WrapSelection("`", "`");
    private void OnLink(object sender, RoutedEventArgs e) => Editor.WrapSelection("[", "](url)");
    private void OnComment(object sender, RoutedEventArgs e) => Editor.WrapSelection("<!-- ", " -->");
    private void OnClearStyle(object sender, RoutedEventArgs e) => Editor.ClearInlineStyle();
    private void OnQuote(object sender, RoutedEventArgs e) => Editor.ToggleBlockquote();
    private void OnUnorderedList(object sender, RoutedEventArgs e) => Editor.ToggleBulletList();
    private void OnOrderedList(object sender, RoutedEventArgs e) => Editor.ToggleOrderedList();
    private void OnInsertParagraphAbove(object sender, RoutedEventArgs e) => Editor.InsertParagraphAbove();
    private void OnInsertParagraphBelow(object sender, RoutedEventArgs e) => Editor.InsertParagraphBelow();
    private void OnCodeBlock(object sender, RoutedEventArgs e) => Editor.WrapSelection("```\n", "\n```");

    private void OnTable(object sender, RoutedEventArgs e)
    {
        ParagraphPopup.IsOpen = false;
        InsertPopup.IsOpen = false;
        var dialog = new TableInsertDialog(_localizationService) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is (int rows, int cols))
            Editor.InsertText(MarkdownInsertService.GenerateTable(rows, cols));
    }

    private void OnHorizontalRule(object sender, RoutedEventArgs e) => Editor.InsertHorizontalRule();
    private void OnFind(object sender, RoutedEventArgs e) => ShowSearchPanel();

    private void OnToggleSidebarFromMenu(object sender, RoutedEventArgs e)
    {
        ViewPopup.IsOpen = false;
        _viewModel.IsSidebarOpen = !_viewModel.IsSidebarOpen;
        AnimateSidebar(_viewModel.IsSidebarOpen ? SidebarWidth : 0);
    }

    private void ShowSearchPanel()
    {
        UpdateSearchPanelPosition();
        SearchPanel.Visibility = Visibility.Visible;
        SearchInput.Focus();
        SearchInput.SelectAll();
    }

    private void UpdateSearchPanelPosition()
    {
        var previewAndSplitter = Editor.ActualWidth - Editor.TextBox.ActualWidth;
        SearchPanel.Margin = new Thickness(0, 4, previewAndSplitter + 8, 0);
    }

    private void HideSearchPanel()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        SearchInput.Text = "";
        _searchResultSet = SearchResultSet.Empty;
        SearchCount.Text = "";
    }

    private void OnSearchInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPanel.Visibility != Visibility.Visible) return;
        PerformSearch();
    }

    private void OnSearchInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var direction = Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1;
            Dispatcher.BeginInvoke(() => NavigateSearch(direction));
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideSearchPanel();
        }
    }

    private void OnSearchNext(object sender, RoutedEventArgs e) => NavigateSearch(1);
    private void OnSearchPrevious(object sender, RoutedEventArgs e) => NavigateSearch(-1);
    private void OnSearchClose(object sender, RoutedEventArgs e) => HideSearchPanel();

    private void PerformSearch()
    {
        var searchText = SearchInput.Text;
        if (string.IsNullOrEmpty(searchText))
        {
            _searchResultSet = SearchResultSet.Empty;
            SearchCount.Text = "";
            return;
        }

        _searchResultSet = MarkdownSearchService.FindMatches(Editor.TextBox.Text, searchText);
        if (_searchResultSet.Matches.Count > 0)
            Editor.TextBox.Select(_searchResultSet.Matches[0], searchText.Length);

        UpdateSearchCount();
    }

    private void NavigateSearch(int direction)
    {
        if (_searchResultSet.Matches.Count == 0) { PerformSearch(); return; }
        _searchResultSet = MarkdownSearchService.Move(_searchResultSet, direction);
        GoToMatch(_searchResultSet.CurrentIndex);
        UpdateSearchCount();
    }

    private void GoToMatch(int index)
    {
        var textBox = Editor.TextBox;
        var pos = _searchResultSet.Matches[index];
        var length = SearchInput.Text.Length;
        textBox.Select(pos, length);
        textBox.Focus();
        Dispatcher.BeginInvoke(() => SearchInput.Focus());
    }

    private void UpdateSearchCount()
    {
        SearchCount.Text = _searchResultSet.Matches.Count > 0
            ? $"{_searchResultSet.CurrentIndex + 1}/{_searchResultSet.Matches.Count}"
            : _localizationService.GetString("MainWindow.NoResults");
    }

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        _viewModel.IsSidebarOpen = !_viewModel.IsSidebarOpen;
        AnimateSidebar(_viewModel.IsSidebarOpen ? SidebarWidth : 0);
    }

    private void AnimateSidebar(double targetWidth)
    {
        var isOpening = targetWidth > 0;
        var currentOffset = SidebarTranslateTransform.X;
        SidebarTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SidebarColumn.Width = new GridLength(isOpening ? SidebarWidth : 0);

        var anim = new DoubleAnimation
        {
            From = currentOffset,
            To = isOpening ? 0 : -SidebarWidth,
            Duration = TimeSpan.FromMilliseconds(SidebarAnimMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };

        anim.Completed += (_, _) =>
        {
            SidebarTranslateTransform.X = isOpening ? 0 : -SidebarWidth;
            SidebarColumn.Width = new GridLength(isOpening ? SidebarWidth : 0);
        };

        SidebarTranslateTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void OpenSidebar()
    {
        if (_viewModel.IsSidebarOpen)
            return;

        _viewModel.IsSidebarOpen = true;
        AnimateSidebar(SidebarWidth);
    }

    private void OnTabOutline(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        TabFiles.FontWeight = FontWeights.Normal;
        TabFiles.Foreground = (Brush)FindResource("TextSecondaryBrush");
        TabOutline.FontWeight = FontWeights.SemiBold;
        TabOutline.Foreground = (Brush)FindResource("TextPrimaryBrush");
        FilesUnderline.Visibility = Visibility.Collapsed;
        OutlineUnderline.Visibility = Visibility.Visible;

        FilesPanel.Visibility = Visibility.Collapsed;
        OutlinePanel.Visibility = Visibility.Visible;
        RenderOutline();
    }

    private void OnTabFiles(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowFilesTab();
    }

    private void ShowFilesTab()
    {
        TabFiles.FontWeight = FontWeights.SemiBold;
        TabFiles.Foreground = (Brush)FindResource("TextPrimaryBrush");
        TabOutline.FontWeight = FontWeights.Normal;
        TabOutline.Foreground = (Brush)FindResource("TextSecondaryBrush");
        FilesUnderline.Visibility = Visibility.Visible;
        OutlineUnderline.Visibility = Visibility.Collapsed;

        FilesPanel.Visibility = Visibility.Visible;
        OutlinePanel.Visibility = Visibility.Collapsed;
        UpdateFilesPanelState();
    }

    private void UpdateFilesPanelState()
    {
        var hasWorkspaceFiles = _workspaceViewModel.Root?.Children.Count > 0 == true;
        FilesTree.Visibility = hasWorkspaceFiles ? Visibility.Visible : Visibility.Collapsed;
        FilesEmptyPanel.Visibility = hasWorkspaceFiles ? Visibility.Collapsed : Visibility.Visible;

        if (_workspaceViewModel.Root is null)
        {
            FilesEmptyTitle.Text = _localizationService.GetString("MainWindow.NoFolderOpened");
            FilesEmptyHint.Text = _localizationService.GetString("MainWindow.OpenFolderHint");
            return;
        }

        FilesEmptyTitle.Text = _localizationService.GetString("MainWindow.NoMarkdownFiles");
        FilesEmptyHint.Text = _localizationService.GetString("MainWindow.NoMarkdownFilesHint");
    }

    private void OnFilesTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_workspaceViewModel.IsSelectingNode)
            return;

        if (e.NewValue is WorkspaceTreeNode { IsDirectory: false } node)
            OpenFilePath(node.FullPath);
    }

    private async void OnFilesTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: WorkspaceTreeNode node })
            await LoadWorkspaceNodeChildrenAsync(node);
    }

    private void OnMarkdownChanged(object? sender, EventArgs e)
    {
        if (!_viewModel.IsLoadingFile)
        {
            _viewModel.MarkDirty();
            UpdateTitle();
        }
    }

    private void OnPreviewRendered(object? sender, PreviewRenderedEventArgs e)
    {
        _outlineEntries = OutlineBuilder.Build(e.Markdown, e.Blocks);
        if (OutlinePanel.Visibility == Visibility.Visible)
            RenderOutline();
    }

    private void RenderOutline()
    {
        OutlineList.Children.Clear();

        if (_outlineEntries.Count == 0)
        {
            AddEmptyOutlineMessage();
            return;
        }

        foreach (var entry in _outlineEntries)
        {
            var indent = (entry.Level - 1) * 12;
            var fontSize = Math.Max(11, 14 - entry.Level);

            var btn = new Button
            {
                Style = (Style)FindResource("SidebarItemStyle"),
                Content = new TextBlock
                {
                    Text = entry.Text,
                    FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                    FontSize = fontSize,
                    FontWeight = entry.Level <= 2 ? FontWeights.SemiBold : FontWeights.Normal,
                },
                Margin = new Thickness(indent, 0, 0, 0),
            };
            btn.Click += (_, _) => Editor.GoToLine(entry.Line);
            OutlineList.Children.Add(btn);
        }
    }

    private void AddEmptyOutlineMessage()
    {
        OutlineList.Children.Add(new TextBlock
        {
            Text = _localizationService.GetString("MainWindow.NoHeadingsFound"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 32, 0, 0),
        });
    }
}
