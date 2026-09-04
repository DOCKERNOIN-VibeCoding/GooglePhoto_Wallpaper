using System.IO;
using System.Threading;
using System.Windows;
using GooglePhotoWallpaper.Services;

namespace GooglePhotoWallpaper;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private AppServices? _services;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One rotator per user, or two copies would fight over the same monitors.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\GooglePhotoWallpaper", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Google Photo Wallpaper가 이미 실행 중입니다.\n작업 표시줄 알림 영역의 아이콘을 확인하세요.",
                "Google Photo Wallpaper",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show(
                $"예기치 못한 오류가 발생했습니다.\n\n{args.Exception.Message}",
                "Google Photo Wallpaper",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        _services = new AppServices();
        _tray = new TrayIcon(_services);

        // Keep the Windows startup entry in step with the saved preference: the exe may have moved
        // since it was last written.
        try
        {
            if (_services.Settings.StartWithWindows != StartupRegistration.IsEnabled)
            {
                StartupRegistration.Set(_services.Settings.StartWithWindows);
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }

        _services.Rotator.Start(_services.CreateSource());

        // Nothing to show yet, so bring the user straight to setup on a first run.
        bool firstRun = _services.Library.Count == 0;
        if (firstRun || e.Args.Contains("--settings"))
        {
            _tray.ShowSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _services?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Appends to a local log. Best effort - logging must never take the app down.</summary>
    public static void Log(Exception exception)
    {
        try
        {
            File.AppendAllText(
                AppPaths.LogFile,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nothing useful to do here.
        }
    }
}
