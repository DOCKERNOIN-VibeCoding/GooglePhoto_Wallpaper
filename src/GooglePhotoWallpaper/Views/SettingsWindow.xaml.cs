using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using GooglePhotoWallpaper.Interop;
using GooglePhotoWallpaper.Models;
using GooglePhotoWallpaper.Services;
using GooglePhotoWallpaper.Services.Sources;
using Forms = System.Windows.Forms;

namespace GooglePhotoWallpaper.Views;

public sealed class MonitorPreviewItem
{
    public required string Title { get; init; }
    public required string Caption { get; init; }
    public BitmapImage? Thumbnail { get; init; }
}

public partial class SettingsWindow : Window
{
    private static readonly int[] IntervalPresets = [1, 5, 10, 15, 30, 60, 120, 180, 360, 720, 1440];

    private static readonly int[] AlbumSyncPresets = [1, 5, 10, 15, 30, 60, 180, 360, 720, 1440];

    /// <summary>
    /// Measured size of one compressed fetch of a shared album page. Google sends no ETag and marks
    /// the page no-store, so every check pays this in full - worth showing before someone sets the
    /// interval to one minute.
    /// </summary>
    private const double AlbumPageKilobytes = 209;

    private static readonly (PhotoFitMode Value, string Label, string Hint)[] FitOptions =
    [
        (PhotoFitMode.Crop, "채우기 — 화면을 꽉 채움",
            "화면 밖으로 나가는 부분은 잘립니다. 세로 사진은 위아래가 크게 잘려나갑니다."),
        (PhotoFitMode.BlurredPadding, "전체 보이기 — 블러 배경",
            "사진 전체를 보여주고, 남는 좌우 여백을 같은 사진을 확대·흐리게 한 것으로 채웁니다."),
        (PhotoFitMode.SolidPadding, "전체 보이기 — 검은 여백",
            "사진 전체를 보여주고, 남는 여백은 검게 둡니다."),
        (PhotoFitMode.WindowsFit, "맞춤 (Windows)",
            "Windows 기본 맞춤. 여백은 바탕 색으로 채워집니다."),
        (PhotoFitMode.WindowsStretch, "늘이기 (Windows)",
            "화면 비율에 맞춰 늘립니다. 사진이 찌그러집니다."),
        (PhotoFitMode.WindowsCenter, "가운데 (Windows)",
            "원래 크기 그대로 화면 가운데에 놓습니다."),
        (PhotoFitMode.WindowsTile, "바둑판 (Windows)", "사진을 반복해서 깝니다."),
        (PhotoFitMode.WindowsSpan, "확장 (Windows)",
            "사진 한 장을 여러 모니터에 걸쳐 펼칩니다. 모니터별 배치와는 어울리지 않습니다."),
    ];

    private readonly AppServices _services;
    private readonly ObservableCollection<MonitorPreviewItem> _preview = [];
    private CancellationTokenSource? _busyCancellation;
    private bool _loading;

    public SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        MonitorPreview.ItemsSource = _preview;

        foreach ((PhotoFitMode _, string label, string _) in FitOptions)
        {
            FitCombo.Items.Add(label);
        }

        foreach (int minutes in IntervalPresets)
        {
            IntervalCombo.Items.Add(minutes.ToString(CultureInfo.InvariantCulture));
        }

        foreach (int minutes in AlbumSyncPresets)
        {
            AlbumSyncCombo.Items.Add(minutes.ToString(CultureInfo.InvariantCulture));
        }

        LoadFromSettings();
        _services.Rotator.StatusChanged += OnRotatorStatusChanged;
        Closed += (_, _) =>
        {
            _services.Rotator.StatusChanged -= OnRotatorStatusChanged;
            _busyCancellation?.Cancel();
        };

        RefreshStatus();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            AppSettings s = _services.Settings;

            SourceAlbumRadio.IsChecked = s.Source != PhotoSourceKind.LocalFolder;
            SourceFolderRadio.IsChecked = s.Source == PhotoSourceKind.LocalFolder;

            AlbumUrlBox.Text = s.SharedAlbumUrl ?? string.Empty;
            AlbumSyncCombo.Text = s.AlbumSyncMinutes.ToString(CultureInfo.InvariantCulture);
            UpdateAlbumTrafficHint();

            FolderPathBox.Text = s.LocalFolderPath ?? string.Empty;
            RecursiveCheck.IsChecked = s.LocalFolderRecursive;

            IntervalCombo.Text = s.IntervalMinutes.ToString(CultureInfo.InvariantCulture);

            SequentialRadio.IsChecked = s.MonitorMode == MonitorAssignmentMode.Sequential;
            MirrorRadio.IsChecked = s.MonitorMode == MonitorAssignmentMode.Mirror;
            SingleRadio.IsChecked = s.MonitorMode == MonitorAssignmentMode.Single;
            ShuffleCheck.IsChecked = s.Shuffle;

            PopulateMonitorList(s.TargetMonitorIndex);

            int fitIndex = Array.FindIndex(FitOptions, f => f.Value == s.FitMode);
            FitCombo.SelectedIndex = fitIndex >= 0 ? fitIndex : 0;
            UpdateFitHint();

            StartupCheck.IsChecked = s.StartWithWindows;
            ChangeOnStartCheck.IsChecked = s.ChangeOnStartup;

        }
        finally
        {
            _loading = false;
        }
    }

    private AppSettings CollectSettings()
    {
        AppSettings s = _services.Settings;

        s.Source = SourceFolderRadio.IsChecked == true
            ? PhotoSourceKind.LocalFolder
            : PhotoSourceKind.SharedAlbum;

        s.SharedAlbumUrl = string.IsNullOrWhiteSpace(AlbumUrlBox.Text) ? null : AlbumUrlBox.Text.Trim();

        if (int.TryParse(AlbumSyncCombo.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int syncMinutes) && syncMinutes >= 1)
        {
            s.AlbumSyncMinutes = Math.Min(syncMinutes, 60 * 24);
        }

        s.LocalFolderPath = string.IsNullOrWhiteSpace(FolderPathBox.Text) ? null : FolderPathBox.Text;
        s.LocalFolderRecursive = RecursiveCheck.IsChecked == true;

        if (int.TryParse(IntervalCombo.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
            && minutes >= 1)
        {
            s.IntervalMinutes = Math.Min(minutes, 60 * 24);
        }

        s.MonitorMode = true switch
        {
            _ when MirrorRadio.IsChecked == true => MonitorAssignmentMode.Mirror,
            _ when SingleRadio.IsChecked == true => MonitorAssignmentMode.Single,
            _ => MonitorAssignmentMode.Sequential,
        };

        if (TargetMonitorCombo.SelectedIndex >= 0)
        {
            s.TargetMonitorIndex = TargetMonitorCombo.SelectedIndex;
        }

        s.Shuffle = ShuffleCheck.IsChecked == true;

        if (FitCombo.SelectedIndex >= 0 && FitCombo.SelectedIndex < FitOptions.Length)
        {
            s.FitMode = FitOptions[FitCombo.SelectedIndex].Value;
        }

        s.StartWithWindows = StartupCheck.IsChecked == true;
        s.ChangeOnStartup = ChangeOnStartCheck.IsChecked == true;

        return s;
    }

    /// <summary>
    /// Lists the monitors that are attached right now, so the single-monitor choice reads
    /// "모니터 2 (1920x1080)" rather than an index.
    /// </summary>
    private void PopulateMonitorList(int selectedIndex)
    {
        TargetMonitorCombo.Items.Clear();

        IReadOnlyList<MonitorInfo> monitors = _services.Wallpaper.GetMonitors();
        foreach (MonitorInfo monitor in monitors)
        {
            TargetMonitorCombo.Items.Add(monitor.DisplayName);
        }

        if (monitors.Count > 0)
        {
            TargetMonitorCombo.SelectedIndex = Math.Clamp(selectedIndex, 0, monitors.Count - 1);
        }
    }

    private void OnFitModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateFitHint();
    }

    private void UpdateFitHint()
    {
        FitHint.Text = FitCombo.SelectedIndex >= 0 && FitCombo.SelectedIndex < FitOptions.Length
            ? FitOptions[FitCombo.SelectedIndex].Hint
            : string.Empty;
    }

    private void OnRotatorStatusChanged(object? sender, RotationStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyStatus(status));
            return;
        }

        ApplyStatus(status);
    }

    private void RefreshStatus() => ApplyStatus(_services.Rotator.Status);

    private void ApplyStatus(RotationStatus status)
    {
        string next = status.IsRunning && status.NextChangeAt is { } at
            ? $"다음 변경 {at.ToLocalTime():HH:mm:ss}"
            : "일시 정지됨";

        StatusLine.Text = status.PhotoCount == 0
            ? "사진이 아직 없습니다. 아래에서 사진을 선택하세요."
            : $"사진 {status.PhotoCount}장 · 모니터 {status.MonitorCount}대 · {next}";

        if (!string.IsNullOrEmpty(status.LastError))
        {
            StatusLine.Text = status.LastError;
        }

        RebuildPreview(status);
    }

    private void RebuildPreview(RotationStatus status)
    {
        _preview.Clear();

        IReadOnlyList<MonitorInfo> monitors = _services.Wallpaper.GetMonitors();
        IReadOnlyList<PhotoItem> photos = _services.Library.Photos;

        if (monitors.Count == 0 || photos.Count == 0)
        {
            PreviewHint.Text = photos.Count == 0
                ? "사진을 선택하면 어떤 사진이 어느 모니터에 붙는지 여기에서 보여드립니다."
                : "연결된 모니터를 찾지 못했습니다.";
            return;
        }

        MonitorAssignmentMode mode = _services.Settings.MonitorMode;
        int mirrored = _services.Engine.CurrentMirroredIndex();
        int single = Math.Clamp(_services.Settings.TargetMonitorIndex, 0, monitors.Count - 1);

        IReadOnlyList<int> sequential = mode == MonitorAssignmentMode.Sequential
            ? _services.Engine.CurrentAssignment(monitors.Count)
            : [];

        for (int i = 0; i < monitors.Count; i++)
        {
            // -1 marks a monitor this mode leaves alone.
            int index = mode switch
            {
                MonitorAssignmentMode.Mirror => mirrored,
                MonitorAssignmentMode.Single => i == single ? mirrored : -1,
                _ => i < sequential.Count ? sequential[i] : -1,
            };

            if (index < 0 || index >= photos.Count)
            {
                _preview.Add(new MonitorPreviewItem
                {
                    Title = monitors[i].DisplayName,
                    Caption = "변경하지 않음",
                    Thumbnail = null,
                });
                continue;
            }

            PhotoItem photo = photos[index];
            _preview.Add(new MonitorPreviewItem
            {
                Title = monitors[i].DisplayName,
                Caption = $"{index + 1}번째 사진 · {photo.FileName ?? Path.GetFileName(photo.FilePath)}",
                Thumbnail = LoadThumbnail(photo.FilePath),
            });
        }

        PreviewHint.Text = mode switch
        {
            MonitorAssignmentMode.Mirror => $"모든 모니터에 같은 사진을 표시합니다. 총 {photos.Count}장을 순환합니다.",
            MonitorAssignmentMode.Single =>
                $"{monitors[single].DisplayName}만 바뀝니다. 총 {photos.Count}장을 순환합니다.",
            _ => $"모니터마다 사진이 한 칸씩 밀립니다. 총 {photos.Count}장을 순환합니다.",
        };
    }

    /// <summary>
    /// Loads a small decoded copy. OnLoad caching is essential: without it WPF keeps the file open,
    /// and a locked file cannot be replaced or pruned from the cache later.
    /// </summary>
    private static BitmapImage? LoadThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SetBusy(string? message)
    {
        BusyLine.Text = message ?? string.Empty;
        IsEnabled = true;
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.SaveSettings(CollectSettings());
        RefreshStatus();
    }

    private void OnAlbumSyncIntervalChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateAlbumTrafficHint();
    }

    /// <summary>
    /// Spells out what the chosen interval costs per day, so a one-minute setting is a decision
    /// rather than a surprise on the next mobile bill.
    /// </summary>
    private void UpdateAlbumTrafficHint()
    {
        if (!int.TryParse(AlbumSyncCombo.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int minutes) || minutes < 1)
        {
            AlbumTrafficHint.Text = string.Empty;
            return;
        }

        double perDayMb = 1440.0 / minutes * AlbumPageKilobytes / 1024.0;
        AlbumTrafficHint.Text = $"분마다 확인 · 하루 약 {perDayMb:0.#} MB";
    }

    private async void OnSyncAlbum(object sender, RoutedEventArgs e)
    {
        try
        {
            SourceAlbumRadio.IsChecked = true;
            _services.SaveSettings(CollectSettings());

            if (_services.CreateSource() is not SharedAlbumSource source)
            {
                SetBusy("공유 앨범 링크를 먼저 붙여넣으세요.");
                return;
            }

            _busyCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            IReadOnlyList<PhotoItem> photos =
                await source.RefreshAsync(new Progress<string>(SetBusy), _busyCancellation.Token);

            _services.Rotator.SetPhotos(photos);
            _services.Library.PruneCache(AppPaths.CacheDirectory);
            _services.Composer.Prune(photos);

            SetBusy(source.LastNewCount > 0
                ? $"{photos.Count}장을 적용했습니다. (새 사진 {source.LastNewCount}장)"
                : $"{photos.Count}장을 적용했습니다.");
            RefreshStatus();
        }
        catch (OperationCanceledException)
        {
            SetBusy("동기화가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetBusy(null);
            ShowError("공유 앨범을 읽지 못했습니다", ex.Message);
        }
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "배경화면으로 쓸 사진이 들어 있는 폴더를 선택하세요.",
            UseDescriptionForTitle = true,
            SelectedPath = FolderPathBox.Text,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            FolderPathBox.Text = dialog.SelectedPath;
            _services.SaveSettings(CollectSettings());
            OnRescanFolder(sender, e);
        }
    }

    private async void OnRescanFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            _services.SaveSettings(CollectSettings());

            if (_services.CreateSource() is not LocalFolderSource source)
            {
                SetBusy("폴더를 먼저 선택하세요.");
                return;
            }

            IReadOnlyList<PhotoItem> photos =
                await source.RefreshAsync(new Progress<string>(SetBusy), CancellationToken.None);

            if (photos.Count == 0)
            {
                SetBusy("폴더에서 사진을 찾지 못했습니다.");
                return;
            }

            _services.Rotator.SetPhotos(photos);
            SetBusy($"{photos.Count}장을 적용했습니다.");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowError("폴더를 읽지 못했습니다", ex.Message);
        }
    }

    private void OnOpenCacheFolder(object sender, RoutedEventArgs e)
        => OpenUrl(AppPaths.CacheDirectory);

    private void OnAdvanceNow(object sender, RoutedEventArgs e)
    {
        _services.Rotator.AdvanceNow();
        RefreshStatus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            AppSettings settings = CollectSettings();

            if (settings.StartWithWindows != StartupRegistration.IsEnabled)
            {
                StartupRegistration.Set(settings.StartWithWindows);
            }

            _services.SaveSettings(settings);
            _services.Rotator.ApplyCurrent();

            SetBusy("저장했습니다.");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowError("설정을 저장하지 못했습니다", ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void ShowError(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
