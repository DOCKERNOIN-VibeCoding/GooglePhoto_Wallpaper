using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using GooglePhotoWallpaper.Interop;
using GooglePhotoWallpaper.Models;
using GooglePhotoWallpaper.Services;
using GooglePhotoWallpaper.Services.Google;
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

    private static readonly (DesktopWallpaperPosition Value, string Label)[] PositionOptions =
    [
        (DesktopWallpaperPosition.Fill, "채우기 (Fill)"),
        (DesktopWallpaperPosition.Fit, "맞춤 (Fit)"),
        (DesktopWallpaperPosition.Stretch, "늘이기 (Stretch)"),
        (DesktopWallpaperPosition.Center, "가운데 (Center)"),
        (DesktopWallpaperPosition.Tile, "바둑판 (Tile)"),
        (DesktopWallpaperPosition.Span, "확장 (Span)"),
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

        foreach ((DesktopWallpaperPosition _, string label) in PositionOptions)
        {
            PositionCombo.Items.Add(label);
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

            SourceAlbumRadio.IsChecked = s.Source == PhotoSourceKind.SharedAlbum;
            SourceGoogleRadio.IsChecked = s.Source == PhotoSourceKind.GooglePhotos;
            SourceFolderRadio.IsChecked = s.Source == PhotoSourceKind.LocalFolder;

            AlbumUrlBox.Text = s.SharedAlbumUrl ?? string.Empty;
            AlbumSyncCombo.Text = s.AlbumSyncMinutes.ToString(CultureInfo.InvariantCulture);
            UpdateAlbumTrafficHint();

            FolderPathBox.Text = s.LocalFolderPath ?? string.Empty;
            RecursiveCheck.IsChecked = s.LocalFolderRecursive;

            IntervalCombo.Text = s.IntervalMinutes.ToString(CultureInfo.InvariantCulture);

            SequentialRadio.IsChecked = !s.MirrorAllMonitors;
            MirrorRadio.IsChecked = s.MirrorAllMonitors;
            ShuffleCheck.IsChecked = s.Shuffle;

            int positionIndex = Array.FindIndex(PositionOptions, p => p.Value == s.Position);
            PositionCombo.SelectedIndex = positionIndex >= 0 ? positionIndex : 0;

            CredUserRadio.IsChecked = s.CredentialMode == OAuthCredentialMode.UserProvided;
            CredBundledRadio.IsChecked = s.CredentialMode == OAuthCredentialMode.Bundled;
            CredBundledRadio.IsEnabled = OAuthClientConfig.HasBundledClient;
            BundledHint.Text = OAuthClientConfig.HasBundledClient
                ? "이 빌드에는 검증된 클라이언트가 포함되어 있습니다."
                : "이 빌드에는 포함된 클라이언트가 없습니다. 배포용 공개 빌드는 자격증명을 담지 않습니다.";

            StartupCheck.IsChecked = s.StartWithWindows;
            ChangeOnStartCheck.IsChecked = s.ChangeOnStartup;

            RefreshAccountLine();
            RefreshClientLine();
        }
        finally
        {
            _loading = false;
        }
    }

    private AppSettings CollectSettings()
    {
        AppSettings s = _services.Settings;

        s.Source = true switch
        {
            _ when SourceAlbumRadio.IsChecked == true => PhotoSourceKind.SharedAlbum,
            _ when SourceFolderRadio.IsChecked == true => PhotoSourceKind.LocalFolder,
            _ => PhotoSourceKind.GooglePhotos,
        };

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

        s.MirrorAllMonitors = MirrorRadio.IsChecked == true;
        s.Shuffle = ShuffleCheck.IsChecked == true;

        if (PositionCombo.SelectedIndex >= 0 && PositionCombo.SelectedIndex < PositionOptions.Length)
        {
            s.Position = PositionOptions[PositionCombo.SelectedIndex].Value;
        }

        s.CredentialMode = CredBundledRadio.IsChecked == true
            ? OAuthCredentialMode.Bundled
            : OAuthCredentialMode.UserProvided;

        s.StartWithWindows = StartupCheck.IsChecked == true;
        s.ChangeOnStartup = ChangeOnStartCheck.IsChecked == true;

        return s;
    }

    private void RefreshAccountLine()
    {
        bool connected = _services.IsGoogleConnected;
        string? email = connected ? _services.ConnectedAccount : null;

        AccountLine.Text = connected
            ? $"연결됨: {email ?? "Google 계정"}"
            : "연결되지 않음";

        ConnectButton.Content = connected ? "다시 연결" : "계정 연결";
        DisconnectButton.IsEnabled = connected;

        // Picking signs the user in on its own when needed, so this stays available even with no
        // stored token - it only needs an OAuth client to sign in against.
        PickPhotosButton.IsEnabled = _services.HasOAuthClient;
    }

    private void RefreshClientLine()
    {
        if (File.Exists(AppPaths.ClientSecretFile))
        {
            try
            {
                OAuthClientConfig config = OAuthClientConfig.FromClientSecretFile(AppPaths.ClientSecretFile);
                // Only the project id is shown. The client id is not a password, but there is no
                // reason to render it on screen either.
                ClientStatusLine.Text = string.IsNullOrEmpty(config.ProjectId)
                    ? "등록됨"
                    : $"등록됨 (프로젝트: {config.ProjectId})";
                ClearClientButton.IsEnabled = true;
                return;
            }
            catch (Exception)
            {
                ClientStatusLine.Text = "등록된 파일을 읽을 수 없습니다";
                ClearClientButton.IsEnabled = true;
                return;
            }
        }

        ClientStatusLine.Text = "등록되지 않음";
        ClearClientButton.IsEnabled = false;
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

        bool mirror = _services.Settings.MirrorAllMonitors;
        IReadOnlyList<int> assignment = mirror
            ? Enumerable.Repeat(_services.Engine.CurrentMirroredIndex(), monitors.Count).ToArray()
            : _services.Engine.CurrentAssignment(monitors.Count);

        for (int i = 0; i < monitors.Count && i < assignment.Count; i++)
        {
            int index = assignment[i];
            if (index < 0 || index >= photos.Count)
            {
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

        PreviewHint.Text = mirror
            ? "모든 모니터에 같은 사진을 표시하는 중입니다."
            : $"모니터마다 사진이 한 칸씩 밀립니다. 총 {photos.Count}장을 순환합니다.";
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

    private async void OnConnectAccount(object sender, RoutedEventArgs e)
    {
        try
        {
            _services.SaveSettings(CollectSettings());
            OAuthClientConfig client = _services.RequireOAuthClient();

            SetBusy("브라우저에서 Google 로그인을 완료해 주세요...");
            _busyCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            StoredToken token = await _services.OAuth.SignInAsync(client, _busyCancellation.Token);

            SetBusy($"연결되었습니다: {token.AccountEmail ?? "Google 계정"}");
            RefreshAccountLine();
        }
        catch (OperationCanceledException)
        {
            SetBusy("로그인이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetBusy(null);
            ShowError("Google 계정 연결 실패", ex.Message);
        }
    }

    private async void OnDisconnectAccount(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Google 계정 연결을 해제할까요?\n이미 내려받은 사진은 그대로 남습니다.",
                "연결 해제",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _services.OAuth.RevokeAsync(CancellationToken.None);
            SetBusy("연결을 해제했습니다.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        RefreshAccountLine();
    }

    private async void OnPickPhotos(object sender, RoutedEventArgs e)
    {
        try
        {
            _services.SaveSettings(CollectSettings());
            OAuthClientConfig client = _services.RequireOAuthClient();

            var source = new GooglePhotosPickerSource(
                _services.OAuth, _services.Picker, _services.Wallpaper, client);

            _busyCancellation = new CancellationTokenSource(TimeSpan.FromHours(1));
            var progress = new Progress<string>(SetBusy);

            PickPhotosButton.IsEnabled = false;
            IReadOnlyList<PhotoItem> photos = await source.RefreshAsync(progress, _busyCancellation.Token);

            _services.Rotator.SetPhotos(photos);
            _services.Library.PruneCache(AppPaths.CacheDirectory);

            SetBusy($"{photos.Count}장을 적용했습니다.");
            RefreshStatus();
        }
        catch (OperationCanceledException)
        {
            SetBusy("사진 선택이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetBusy(null);
            ShowError("사진을 가져오지 못했습니다", ex.Message);
        }
        finally
        {
            PickPhotosButton.IsEnabled = _services.HasOAuthClient;
            RefreshAccountLine();
        }
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

    private void OnImportClientSecret(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Google Cloud에서 받은 client_secret.json 선택",
            Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _services.ImportClientSecret(dialog.FileName);
            CredUserRadio.IsChecked = true;
            _services.SaveSettings(CollectSettings());
            RefreshClientLine();
            SetBusy("OAuth 클라이언트를 등록했습니다. 이제 계정을 연결하세요.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowError("client_secret.json을 등록하지 못했습니다", ex.Message);
        }
    }

    private void OnClearClientSecret(object sender, RoutedEventArgs e)
    {
        try
        {
            _services.ClearClientSecret();
            RefreshClientLine();
            SetBusy("등록된 OAuth 클라이언트를 삭제했습니다.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void OnOpenSetupGuide(object sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/DOCKERNOIN-VibeCoding/GooglePhoto_Wallpaper/blob/main/docs/GOOGLE-CLOUD-SETUP.md");

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
