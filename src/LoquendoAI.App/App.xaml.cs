using System.Windows;
using System.Windows.Threading;

namespace LoquendoAI.App;

public partial class App : Application
{
    public App()
    {
        // Portable version: every file of the app (Infrastructure caches too) goes to «datos» next to the .exe.
        LoquendoAI.Infrastructure.AppDataFolder.Folder = AppPaths.DataFolder;
        UseBundledFfmpeg();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Failures outside the UI thread (background tasks nobody awaited, worker threads) used to
        // vanish or close the app without a trace: they are recorded in the same error log.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Record(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception) ErrorLog.Record(exception);
        };
    }

    /// <summary>
    /// The published version brings FFmpeg in tools\ffmpeg (1.4.2). Putting that folder first in this process's PATH
    /// makes every «ffmpeg»/«ffprobe» the app starts use it, before any other FFmpeg of the PC. Without the folder
    /// (running from the source code) the PATH is left as it is.
    /// </summary>
    private static void UseBundledFfmpeg()
    {
        try
        {
            var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
            if (!System.IO.File.Exists(System.IO.Path.Combine(folder, "ffmpeg.exe"))) return;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            Environment.SetEnvironmentVariable("PATH", folder + System.IO.Path.PathSeparator + path);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or ArgumentException) { /* keep the PATH */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Before the main window exists, so it opens already in the saved theme.
        ThemeManager.Initialize(this);
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = ErrorLog.Record(e.Exception);

        MessageBox.Show(
            $"Ocurrió un error en la interfaz: {ErrorLog.Summary(e.Exception)}{Environment.NewLine}" +
            $"Puedes enviar el registro para investigarlo: {logPath}",
            "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
