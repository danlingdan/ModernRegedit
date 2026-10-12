using System.Globalization;
using Microsoft.UI.Xaml;
using ModernRegedit.Services;

namespace ModernRegedit;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash($"UnobservedTask: {e.Exception}");
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash($"Fatal ({(e.IsTerminating ? "terminating" : "observerd")}): {e.ExceptionObject}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash($"XAML: {e.Message}\n{e.Exception}");
        e.Handled = true;
    }

    /// <summary>崩溃日志写到包私有 LocalCache，便于事后从事件日志之外定位。</summary>
    private static void LogCrash(string text)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModernRegedit");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {text}\n\n");
        }
        catch
        {
            // 日志失败不再抛出。
        }
    }
}
