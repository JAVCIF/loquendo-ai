using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LoquendoAI.App;

/// <summary>
/// Light / dark theme (Loquendo red and white). The colours live in Themes/Light.xaml and Themes/Dark.xaml, which
/// define the same keys; the control styles (Themes/Controls.xaml) use them through DynamicResource, so switching
/// replaces one dictionary and every open window updates at once. «Sistema» follows the Windows app mode.
/// </summary>
internal static class ThemeManager
{
    public const string SystemChoice = "sistema";
    public const string LightChoice = "claro";
    public const string DarkChoice = "oscuro";

    private const string LightUri = "pack://application:,,,/LoquendoAI.App;component/Themes/Light.xaml";
    private const string DarkUri = "pack://application:,,,/LoquendoAI.App;component/Themes/Dark.xaml";

    public static string Choice { get; private set; } = SystemChoice;
    public static bool IsDark { get; private set; }

    /// <summary>Applies the saved theme and makes every window (also the ones built in code) use its colours.</summary>
    public static void Initialize(Application app)
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        Apply(UiSettings.Load().Theme, save: false);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            // «General» fires for many unrelated settings: only reload when the app mode really changed.
            if (Choice == SystemChoice && e.Category == UserPreferenceCategory.General && SystemUsesDarkMode() != IsDark)
                app.Dispatcher.BeginInvoke(new Action(() => Apply(SystemChoice, save: false)));
        };
    }

    /// <summary>«sistema», «claro» or «oscuro».</summary>
    public static void Apply(string? choice, bool save = true)
    {
        Choice = choice is LightChoice or DarkChoice ? choice : SystemChoice;
        IsDark = Choice == DarkChoice || Choice == SystemChoice && SystemUsesDarkMode();
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary { Source = new Uri(IsDark ? DarkUri : LightUri, UriKind.Absolute) };
        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? "";
            if (source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index >= 0) dictionaries[index] = theme;
        else dictionaries.Insert(0, theme);
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
        if (!save) return;
        var settings = UiSettings.Load();
        settings.Theme = Choice;
        settings.Save();
    }

    /// <summary>For controls built in code: <paramref name="property"/> follows the theme colour <paramref name="key"/>.</summary>
    public static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    private const string IconUri = "pack://application:,,,/LoquendoAI.App;component/Assets/loquendo-ai.ico";
    private static ImageSource? _appIcon;

    /// <summary>The app icon (red ball with the L) for every window: title bar, taskbar and Alt+Tab.</summary>
    public static ImageSource? AppIcon
    {
        get
        {
            if (_appIcon is not null) return _appIcon;
            try { _appIcon = BitmapFrame.Create(new Uri(IconUri, UriKind.Absolute)); }
            catch (Exception) { /* Cosmetic only. */ }
            return _appIcon;
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;
        window.Icon ??= AppIcon;
        window.SetResourceReference(Control.BackgroundProperty, "Theme.WindowBackground");
        window.SetResourceReference(Control.ForegroundProperty, "Theme.Text");
        ApplyTitleBar(window);
    }

    /// <summary>Dark title bar in the dark theme (attribute 20 on Windows 10 2004+ / 11, 19 on 1903/1909; older
    /// Windows ignore it). The frame is redrawn at once so a live switch shows without refocusing the window.</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
            const uint noSize = 0x0001, noMove = 0x0002, noZOrder = 0x0004, noActivate = 0x0010, frameChanged = 0x0020;
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, noSize | noMove | noZOrder | noActivate | frameChanged);
        }
        catch (Exception) { /* Cosmetic only. */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Interface preferences kept between sessions (%LOCALAPPDATA%\LoquendoAI\ui.json): theme, window size
/// and the proportions of the resizable panels. A missing or damaged file just means the defaults.</summary>
internal sealed class UiSettings
{
    private static readonly string SettingsPath = AppPaths.PathOf("ui.json");

    public string Theme { get; set; } = ThemeManager.SystemChoice;
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool Maximized { get; set; }
    /// <summary>Width of the Episodes/Scenes column of the Guion tab.</summary>
    public double ScriptNavWidth { get; set; }
    /// <summary>Star weights of the blocks table and of the tools panel (Editor, Preview, Director…).</summary>
    public double ScriptBlocksWeight { get; set; }
    public double ScriptToolsWeight { get; set; }
    /// <summary>Star weights of the sources and assets tables of the Library.</summary>
    public double LibrarySourcesWeight { get; set; }
    public double LibraryAssetsWeight { get; set; }

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath)) ?? new UiSettings();
        }
        catch (Exception) { /* Damaged file: defaults. */ }
        return new UiSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { ErrorLog.Record(ex); }
    }
}
