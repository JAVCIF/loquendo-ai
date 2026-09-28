using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace LoquendoAI.App;

/// <summary>Theme selector, the «Ampliar» mode of the Guion tools panel and the window layout kept between
/// sessions (size, and the proportions the user left with the dividers).</summary>
public partial class MainWindow
{
    private bool _selectingTheme;
    private bool _toolsFocused;
    private GridLength _navWidthBeforeFocus = new(250);
    private GridLength _blocksHeightBeforeFocus = new(2, GridUnitType.Star);

    /// <summary>Before the window is shown: the size it had, kept inside the screen.</summary>
    private void RestoreWindowBounds()
    {
        var settings = UiSettings.Load();
        var area = SystemParameters.WorkArea;
        if (settings.WindowWidth >= MinWidth) Width = Math.Min(settings.WindowWidth, area.Width);
        if (settings.WindowHeight >= MinHeight) Height = Math.Min(settings.WindowHeight, area.Height);
        if (settings.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>On load: the theme in the selector and the panel proportions of the last session.</summary>
    private void RestoreLayout()
    {
        _selectingTheme = true;
        foreach (var item in ThemeCombo.Items)
            if (item is ComboBoxItem { Tag: string tag } choice && tag == ThemeManager.Choice) ThemeCombo.SelectedItem = choice;
        _selectingTheme = false;

        var settings = UiSettings.Load();
        if (settings.ScriptNavWidth >= ScriptNavColumn.MinWidth) ScriptNavColumn.Width = new GridLength(settings.ScriptNavWidth);
        if (settings.ScriptBlocksWeight > 0 && settings.ScriptToolsWeight > 0)
        {
            ScriptBlocksRow.Height = new GridLength(settings.ScriptBlocksWeight, GridUnitType.Star);
            ScriptToolsRow.Height = new GridLength(settings.ScriptToolsWeight, GridUnitType.Star);
        }
        if (settings.LibrarySourcesWeight > 0 && settings.LibraryAssetsWeight > 0)
        {
            LibrarySourcesRow.Height = new GridLength(settings.LibrarySourcesWeight, GridUnitType.Star);
            LibraryAssetsRow.Height = new GridLength(settings.LibraryAssetsWeight, GridUnitType.Star);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        StopDialoguePlayback();
        CommitDialogueEdits();
        SaveDialogueNow(); // the dialogue script saves itself a moment after each change; this saves the last one
        var settings = UiSettings.Load();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
        }
        settings.Maximized = WindowState == WindowState.Maximized;
        // Pixel sizes kept as star weights: the same proportions whatever the window size next time.
        var navWidth = _toolsFocused ? _navWidthBeforeFocus.Value : ScriptNavColumn.ActualWidth;
        if (navWidth >= ScriptNavColumn.MinWidth) settings.ScriptNavWidth = navWidth;
        if (!_toolsFocused && ScriptBlocksRow.ActualHeight > 0 && ScriptToolsRow.ActualHeight > 0)
        {
            settings.ScriptBlocksWeight = ScriptBlocksRow.ActualHeight;
            settings.ScriptToolsWeight = ScriptToolsRow.ActualHeight;
        }
        if (LibrarySourcesRow.ActualHeight > 0 && LibraryAssetsRow.ActualHeight > 0)
        {
            settings.LibrarySourcesWeight = LibrarySourcesRow.ActualHeight;
            settings.LibraryAssetsWeight = LibraryAssetsRow.ActualHeight;
        }
        settings.Save();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingTheme || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: string choice }) return;
        ThemeManager.Apply(choice);
    }

    /// <summary>«Ampliar»: the tools panel (Editor, Preview, Director…) takes the whole tab; again to restore.</summary>
    private void ToggleToolsFocus_Click(object sender, RoutedEventArgs e)
    {
        _toolsFocused = !_toolsFocused;
        if (_toolsFocused)
        {
            _navWidthBeforeFocus = ScriptNavColumn.Width;
            _blocksHeightBeforeFocus = ScriptBlocksRow.Height;
            ScriptNavColumn.MinWidth = 0;
            ScriptNavColumn.Width = new GridLength(0);
            ScriptBlocksRow.MinHeight = 0;
            ScriptBlocksRow.Height = new GridLength(0);
        }
        else
        {
            ScriptNavColumn.MinWidth = 170;
            ScriptNavColumn.Width = _navWidthBeforeFocus;
            ScriptBlocksRow.MinHeight = 110;
            ScriptBlocksRow.Height = _blocksHeightBeforeFocus;
        }
        var visibility = _toolsFocused ? Visibility.Collapsed : Visibility.Visible;
        ScriptNavPanel.Visibility = visibility;
        ScriptNavSplitter.Visibility = visibility;
        ScriptBlocksPane.Visibility = visibility;
        ScriptToolsSplitter.Visibility = visibility;
        ToolsFocusButton.Content = _toolsFocused ? "▼ Restaurar" : "▲ Ampliar panel";
    }
}
