using GooglePhotoWallpaper.Interop;

namespace GooglePhotoWallpaper.Services;

public sealed record MonitorInfo(int Index, string DeviceId, MonitorRect Rect)
{
    public string DisplayName => $"모니터 {Index + 1} ({Rect.Width}×{Rect.Height})";
}

/// <summary>Sets the desktop wallpaper for each monitor independently via the Windows shell.</summary>
public sealed class WallpaperService
{
    private readonly IDesktopWallpaper _wallpaper = DesktopWallpaperFactory.Create();

    /// <summary>
    /// Monitors that are actually attached right now. Windows keeps device paths for displays that
    /// have been disconnected, and those fail GetMonitorRect - so the rect lookup is the filter.
    /// </summary>
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        uint count = _wallpaper.GetMonitorDevicePathCount();

        for (uint i = 0; i < count; i++)
        {
            string id;
            try
            {
                id = _wallpaper.GetMonitorDevicePathAt(i);
            }
            catch (Exception)
            {
                continue;
            }

            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            try
            {
                var rect = _wallpaper.GetMonitorRect(id);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    continue;
                }

                monitors.Add(new MonitorInfo(monitors.Count, id, rect));
            }
            catch (Exception)
            {
                // Disconnected monitor still listed by the shell. Skip it.
            }
        }

        return monitors;
    }

    public void SetWallpaper(string monitorDeviceId, string imagePath)
        => _wallpaper.SetWallpaper(monitorDeviceId, imagePath);

    /// <summary>Applies one image to every monitor at once.</summary>
    public void SetWallpaperOnAllMonitors(string imagePath)
        => _wallpaper.SetWallpaper(null, imagePath);

    public string GetWallpaper(string monitorDeviceId)
    {
        try
        {
            return _wallpaper.GetWallpaper(monitorDeviceId);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public DesktopWallpaperPosition Position
    {
        get
        {
            try
            {
                return _wallpaper.GetPosition();
            }
            catch (Exception)
            {
                return DesktopWallpaperPosition.Fill;
            }
        }
        set => _wallpaper.SetPosition(value);
    }

    /// <summary>Largest monitor dimensions, used to decide how big a copy to download.</summary>
    public (int Width, int Height) GetLargestMonitorSize()
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return (1920, 1080);
        }

        return (monitors.Max(m => m.Rect.Width), monitors.Max(m => m.Rect.Height));
    }
}
