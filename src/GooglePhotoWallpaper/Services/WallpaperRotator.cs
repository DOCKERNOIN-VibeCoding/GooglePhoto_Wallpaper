using System.Threading;
using GooglePhotoWallpaper.Models;
using GooglePhotoWallpaper.Services.Sources;

namespace GooglePhotoWallpaper.Services;

public sealed class RotationStatus
{
    public int PhotoCount { get; init; }
    public int MonitorCount { get; init; }
    public DateTimeOffset? NextChangeAt { get; init; }
    public bool IsRunning { get; init; }
    public string? LastError { get; init; }
    public IReadOnlyList<string> CurrentFiles { get; init; } = [];
}

/// <summary>
/// Drives the whole thing: keeps time, advances the rotation, and pushes each picture to its
/// monitor.
/// </summary>
public sealed class WallpaperRotator : IDisposable
{
    /// <summary>
    /// The timer ticks far more often than the interval and compares wall-clock time, so sleeping or
    /// hibernating the machine cannot swallow a scheduled change.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);

    private readonly WallpaperService _wallpaper;
    private readonly WallpaperComposer _composer = new();
    private readonly RotationEngine _engine;
    private readonly PhotoLibrary _library;
    private readonly SettingsStore _settingsStore;
    private readonly object _gate = new();
    private readonly object _refreshGate = new();

    private System.Threading.Timer? _timer;
    private AppSettings _settings;
    private IPhotoSource? _silentRefreshSource;
    private DateTimeOffset? _nextChangeAt;
    private string? _lastError;
    private IReadOnlyList<string> _currentFiles = [];
    private bool _disposed;

    public WallpaperRotator(
        WallpaperService wallpaper,
        RotationEngine engine,
        PhotoLibrary library,
        SettingsStore settingsStore,
        AppSettings settings)
    {
        _wallpaper = wallpaper;
        _engine = engine;
        _library = library;
        _settingsStore = settingsStore;
        _settings = settings;
    }

    public event EventHandler<RotationStatus>? StatusChanged;

    public RotationStatus Status => BuildStatus();

    /// <summary>
    /// Loads the cached photos and starts the clock. <paramref name="silentRefreshSource"/> is only
    /// set for sources that can re-scan without the user - a local folder, not the Google picker.
    /// </summary>
    public void Start(IPhotoSource? silentRefreshSource)
    {
        lock (_gate)
        {
            _silentRefreshSource = silentRefreshSource?.SupportsSilentRefresh == true
                ? silentRefreshSource
                : null;

            _library.Load();
            _engine.Load(_library.Count, _settings.Shuffle, _settings.RotationOffset);

            _wallpaper.Position = _settings.WindowsPosition;

            _nextChangeAt = DateTimeOffset.UtcNow + _settings.Interval;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(OnTick, null, TimeSpan.Zero, TickInterval);
        }

        // With a shared album configured but nothing cached yet - a first run, or a cache the user
        // cleared - waiting a whole interval before the first sync would leave the desktop empty for
        // up to a day. Pull the album straight away instead.
        if (_library.Count == 0 && _silentRefreshSource is not null)
        {
            Task.Run(() =>
            {
                RefreshSilentSourceIfPossible();
                ApplyCurrent();
            });
        }
        else if (_settings.ChangeOnStartup)
        {
            ApplyCurrent();
        }

        RaiseStatusChanged();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _nextChangeAt = null;
        }

        RaiseStatusChanged();
    }

    /// <summary>Re-reads settings that affect timing and ordering without restarting the app.</summary>
    public void UpdateSettings(AppSettings settings, IPhotoSource? silentRefreshSource)
    {
        bool reshuffle;
        lock (_gate)
        {
            reshuffle = settings.Shuffle != _settings.Shuffle;
            _settings = settings;
            _silentRefreshSource = silentRefreshSource?.SupportsSilentRefresh == true
                ? silentRefreshSource
                : null;

            if (reshuffle)
            {
                _engine.Load(_library.Count, settings.Shuffle, settings.RotationOffset);
            }

            _wallpaper.Position = settings.WindowsPosition;
            _nextChangeAt = DateTimeOffset.UtcNow + settings.Interval;
        }

        RaiseStatusChanged();
    }

    /// <summary>Swaps in a new photo set, e.g. after the user picks again.</summary>
    public void SetPhotos(IReadOnlyList<PhotoItem> photos)
    {
        lock (_gate)
        {
            _library.Replace(photos);
            _engine.Load(photos.Count, _settings.Shuffle);
            _settings.RotationOffset = 0;
            _settingsStore.Save(_settings);
        }

        ApplyCurrent();
    }

    /// <summary>Moves to the next picture immediately and restarts the interval.</summary>
    public void AdvanceNow(int steps = 1)
    {
        _engine.Advance(steps);
        lock (_gate)
        {
            _nextChangeAt = DateTimeOffset.UtcNow + _settings.Interval;
        }

        ApplyCurrent();
    }

    private void OnTick(object? state)
    {
        DateTimeOffset? due;
        lock (_gate)
        {
            due = _nextChangeAt;
        }

        if (due is null || DateTimeOffset.UtcNow < due)
        {
            return;
        }

        lock (_gate)
        {
            _nextChangeAt = DateTimeOffset.UtcNow + _settings.Interval;
        }

        RefreshSilentSourceIfPossible();
        _engine.Advance();
        ApplyCurrent();
    }

    /// <summary>
    /// Picks up files added to a watched folder. Deliberately does nothing for the Google source:
    /// that one needs a browser round trip and must never surprise the user from a timer.
    /// </summary>
    private void RefreshSilentSourceIfPossible()
    {
        IPhotoSource? source;
        lock (_gate)
        {
            source = _silentRefreshSource;
        }

        if (source is null)
        {
            return;
        }

        if (!Monitor.TryEnter(_refreshGate))
        {
            // A refresh is already running - the startup sync and a timer tick can coincide.
            return;
        }

        try
        {
            IReadOnlyList<PhotoItem> photos = source
                .RefreshAsync(null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (photos.Count == 0)
            {
                return;
            }

            int offsetBefore = _engine.Offset;
            _library.Replace(photos);
            _engine.Load(photos.Count, _settings.Shuffle, offsetBefore);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _lastError = ex.Message;
            }
        }
        finally
        {
            Monitor.Exit(_refreshGate);
        }
    }

    /// <summary>Writes the current assignment to the monitors.</summary>
    public void ApplyCurrent()
    {
        try
        {
            IReadOnlyList<PhotoItem> photos = _library.Photos;
            if (photos.Count == 0)
            {
                lock (_gate)
                {
                    _lastError = "표시할 사진이 없습니다. 설정에서 사진을 먼저 선택하세요.";
                    _currentFiles = [];
                }

                RaiseStatusChanged();
                return;
            }

            IReadOnlyList<MonitorInfo> monitors = _wallpaper.GetMonitors();
            if (monitors.Count == 0)
            {
                return;
            }

            var applied = new List<string>(monitors.Count);

            switch (_settings.MonitorMode)
            {
                case MonitorAssignmentMode.Single:
                {
                    // Everything else keeps whatever it is showing, including wallpapers this app
                    // never set.
                    int target = Math.Clamp(_settings.TargetMonitorIndex, 0, monitors.Count - 1);
                    int index = _engine.CurrentMirroredIndex();
                    if (index >= 0 && index < photos.Count)
                    {
                        applied.Add(Apply(monitors[target], photos[index]));
                    }

                    break;
                }

                case MonitorAssignmentMode.Mirror:
                {
                    int index = _engine.CurrentMirroredIndex();
                    if (index >= 0 && index < photos.Count)
                    {
                        // Set per monitor rather than in one call: monitors of different sizes need
                        // their own composed copy of the same photo.
                        foreach (MonitorInfo monitor in monitors)
                        {
                            applied.Add(Apply(monitor, photos[index]));
                        }
                    }

                    break;
                }

                default:
                {
                    IReadOnlyList<int> assignment = _engine.CurrentAssignment(monitors.Count);
                    for (int i = 0; i < monitors.Count && i < assignment.Count; i++)
                    {
                        applied.Add(Apply(monitors[i], photos[assignment[i]]));
                    }

                    break;
                }
            }

            lock (_gate)
            {
                _currentFiles = applied;
                _lastError = null;
                _settings.RotationOffset = _engine.Offset;
            }

            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _lastError = ex.Message;
            }
        }

        RaiseStatusChanged();
    }

    /// <summary>
    /// Puts one photo on one monitor, composing a padded version first when the fit mode calls for
    /// it. Returns the file that ended up on screen.
    /// </summary>
    private string Apply(MonitorInfo monitor, PhotoItem photo)
    {
        string path = _composer.Resolve(
            photo, monitor.Rect.Width, monitor.Rect.Height, _settings.FitMode);

        _wallpaper.SetWallpaper(monitor.DeviceId, path);
        return path;
    }

    private RotationStatus BuildStatus()
    {
        lock (_gate)
        {
            return new RotationStatus
            {
                PhotoCount = _library.Count,
                MonitorCount = _wallpaper.GetMonitors().Count,
                NextChangeAt = _nextChangeAt,
                IsRunning = _timer is not null,
                LastError = _lastError,
                CurrentFiles = _currentFiles,
            };
        }
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, BuildStatus());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
