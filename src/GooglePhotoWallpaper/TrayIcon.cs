using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Resources;
using GooglePhotoWallpaper.Services;
using GooglePhotoWallpaper.Views;
using Forms = System.Windows.Forms;

namespace GooglePhotoWallpaper;

/// <summary>
/// The notification-area icon and its menu. This is the app's only permanent UI - the settings
/// window is opened on demand and closed again.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppServices _services;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _statusItem;

    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public TrayIcon(AppServices services)
    {
        _services = services;

        _statusItem = new Forms.ToolStripMenuItem("준비 중...") { Enabled = false };
        _pauseItem = new Forms.ToolStripMenuItem("일시 정지", null, (_, _) => TogglePause());

        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("설정 열기", null, (_, _) => ShowSettings()));
        menu.Items.Add(new Forms.ToolStripMenuItem("다음 사진으로", null, (_, _) => _services.Rotator.AdvanceNow()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("종료", null, (_, _) => ExitApplication()));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Google Photo Wallpaper",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _services.Rotator.StatusChanged += OnStatusChanged;
        UpdateStatus(_services.Rotator.Status);
    }

    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            return;
        }

        _settingsWindow = new SettingsWindow(_services);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void TogglePause()
    {
        if (_services.Rotator.Status.IsRunning)
        {
            _services.Rotator.Stop();
        }
        else
        {
            _services.Rotator.Start(_services.CreateSource());
        }
    }

    private void OnStatusChanged(object? sender, RotationStatus status)
    {
        // Rotation ticks arrive on a thread-pool thread; NotifyIcon must be touched on the UI thread.
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => UpdateStatus(status));
            return;
        }

        UpdateStatus(status);
    }

    private void UpdateStatus(RotationStatus status)
    {
        if (_disposed)
        {
            return;
        }

        _pauseItem.Text = status.IsRunning ? "일시 정지" : "다시 시작";

        string summary = status.PhotoCount == 0
            ? "사진이 없습니다"
            : $"사진 {status.PhotoCount}장 · 모니터 {status.MonitorCount}대";

        string next = status.IsRunning && status.NextChangeAt is { } at
            ? $" · 다음 변경 {at.ToLocalTime():HH:mm}"
            : status.IsRunning ? string.Empty : " · 일시 정지됨";

        _statusItem.Text = summary + next;

        // The tray tooltip is capped at 63 characters; anything longer is silently dropped.
        string tip = $"Google Photo Wallpaper\n{summary}{next}";
        _notifyIcon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private static Icon LoadIcon()
    {
        try
        {
            StreamResourceInfo? info = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"));
            if (info?.Stream is { } stream)
            {
                using (stream)
                {
                    return new Icon(stream);
                }
            }
        }
        catch (Exception)
        {
            // Fall through to a stock icon rather than failing to start.
        }

        return SystemIcons.Application;
    }

    private void ExitApplication()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Rotator.StatusChanged -= OnStatusChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
