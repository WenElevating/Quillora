using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfMarkdownEditor.Wpf.Localization;

namespace WpfMarkdownEditor.Sample;

public partial class MainWindow
{
    private void BuildThemeList()
    {
        ThemeListPanel.Children.Clear();
        foreach (var theme in ThemeCatalog.Themes)
        {
            var item = new RadioButton
            {
                GroupName = "AppThemes",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Ellipse
                        {
                            Width = 14, Height = 14,
                            Fill = new SolidColorBrush(theme.DotColor),
                            Stroke = new SolidColorBrush(theme.DotBorder),
                            StrokeThickness = 1,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = theme.Name,
                            Margin = new Thickness(10, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    }
                },
                Tag = theme.Name,
                Style = (Style)FindResource("ThemeItemStyle"),
            };
            item.Checked += OnThemeItemSelected;
            ThemeListPanel.Children.Add(item);
        }
    }

    private void OnThemeItemSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string name) return;
        ApplyTheme(name);
        ViewPopup.IsOpen = false;
    }

    private void ApplyTheme(string name)
    {
        var entry = ThemeCatalog.Themes.FirstOrDefault(t => t.Name == name);
        if (entry == null) return;

        _viewModel.CurrentThemeName = name;
        ApplyWindowTheme(entry.IsDark);
        Editor.ApplyTheme(entry.Theme);

        foreach (var child in ThemeListPanel.Children)
        {
            if (child is RadioButton rb)
                rb.IsChecked = (rb.Tag as string) == name;
        }

        SetStatus("Status.ThemeApplied", name);
    }

    private static void ApplyWindowTheme(bool dark)
    {
        var r = Application.Current.Resources;
        SetBrushColor(r, "WindowBackgroundBrush", dark ? "#1E1E1E" : "#F3F3F3");
        SetBrushColor(r, "SurfaceBackgroundBrush", dark ? "#282828" : "#FAFAFA");
        SetBrushColor(r, "CardBackgroundBrush", dark ? "#2D2D2D" : "#FDFDFE");
        SetBrushColor(r, "TextPrimaryBrush", dark ? "#F3F6F8" : "#15171A");
        SetBrushColor(r, "TextSecondaryBrush", dark ? "#B5BBC4" : "#575D66");
        SetBrushColor(r, "TextDisabledBrush", dark ? "#6F7680" : "#838A96");
        SetBrushColor(r, "AccentBrush", dark ? "#60CDFF" : "#005FB8");
        SetBrushColor(r, "DividerBrush", dark ? "#3D3D3D" : "#DDE5F0");
        SetBrushColor(r, "HoverBackgroundBrush", dark ? "#383838" : "#F2F6FB");
        SetBrushColor(r, "PressedBackgroundBrush", dark ? "#434343" : "#E6EEF8");
        SetBrushColor(r, "SegmentBackgroundBrush", dark ? "#404040" : "#E0E0E0");
        SetBrushColor(r, "MenuBorderBrush", dark ? "#4A4A4A" : "#D8E1EC");
    }

    private static void SetBrushColor(ResourceDictionary r, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        r[key] = new SolidColorBrush(color);
    }

    private void BuildLanguageList()
    {
        LanguageListPanel.Children.Clear();
        foreach (var language in SupportedLanguage.All)
        {
            var item = new RadioButton
            {
                Content = _localizationService.GetString(language.DisplayKey),
                Tag = language,
                Style = (Style)FindResource("ThemeItemStyle"),
                IsChecked = language.Equals(_localizationService.CurrentLanguage),
            };
            item.Checked += OnLanguageItemSelected;
            LanguageListPanel.Children.Add(item);
        }
    }

    private void OnLanguageItemSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not SupportedLanguage language)
            return;

        _localizationService.SetLanguage(language);
        _localizationSettingsService.SaveLanguage(language);
        ViewPopup.IsOpen = false;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        BuildLanguageList();
        RefreshLocalizedText();
    }

    private void RefreshLocalizedText()
    {
        _viewModel.RefreshLocalizedText();
        UpdateTitle();
        RefreshStatusText();
        if (OutlinePanel.Visibility == Visibility.Visible)
            RenderOutline();
        UpdateSearchCount();
    }

    private void SetStatus(string key, params object[] args)
    {
        _viewModel.SetStatus(key, args);
        RefreshStatusText();
    }

    private void RefreshStatusText()
    {
        StatusText.Text = _viewModel.StatusText;
    }
}
