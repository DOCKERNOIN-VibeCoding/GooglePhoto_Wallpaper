using System.Diagnostics;
using System.IO;
using GooglePhotoWallpaper.Models;
using GooglePhotoWallpaper.Services.Google;

namespace GooglePhotoWallpaper.Services.Sources;

/// <summary>
/// Pulls pictures from Google Photos through the Picker API.
///
/// The user picks in Google's own UI, which is the only route left to a personal library since
/// photoslibrary.readonly was withdrawn on 2025-03-31. Album *contents* are reachable - searching
/// the picker by album title lists that album's photos - but the picked set is a snapshot: photos
/// added to the album afterwards do not appear until the user picks again.
///
/// Because Google's download links die after roughly an hour, everything is copied to disk during
/// the pick. Rotation then runs entirely offline.
/// </summary>
public sealed class GooglePhotosPickerSource : IPhotoSource
{
    private readonly GoogleOAuthService _oauth;
    private readonly PickerApiClient _picker;
    private readonly WallpaperService _wallpaper;
    private readonly OAuthClientConfig _client;
    private readonly string _cacheDirectory;

    public GooglePhotosPickerSource(
        GoogleOAuthService oauth,
        PickerApiClient picker,
        WallpaperService wallpaper,
        OAuthClientConfig client,
        string? cacheDirectory = null)
    {
        _oauth = oauth;
        _picker = picker;
        _wallpaper = wallpaper;
        _client = client;
        _cacheDirectory = cacheDirectory ?? AppPaths.CacheDirectory;
    }

    public string DisplayName => "Google Photos";

    public bool SupportsSilentRefresh => false;

    /// <summary>Number of pictures fetched by the last successful run.</summary>
    public int LastDownloadedCount { get; private set; }

    public async Task<IReadOnlyList<PhotoItem>> RefreshAsync(
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Google 계정 확인 중...");
        string accessToken = await _oauth.GetAccessTokenAsync(_client, cancellationToken).ConfigureAwait(false);

        progress?.Report("사진 선택 창을 여는 중...");
        PickingSession session = await _picker.CreateSessionAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            OpenPicker(session.PickerUri);

            progress?.Report("브라우저에서 사진을 선택한 뒤 '완료'를 누르세요. 앨범 이름으로 검색할 수 있습니다.");
            session = await _picker.WaitForPickAsync(
                accessToken,
                session,
                remaining => progress?.Report(
                    $"사진 선택을 기다리는 중... (남은 시간 {remaining:mm\\:ss})"),
                cancellationToken).ConfigureAwait(false);

            progress?.Report("선택한 사진 목록을 가져오는 중...");
            IReadOnlyList<PickedPhoto> picked =
                await _picker.ListPickedPhotosAsync(accessToken, session.Id, cancellationToken)
                    .ConfigureAwait(false);

            if (picked.Count == 0)
            {
                throw new PickerApiException(
                    "선택된 사진이 없습니다. 동영상은 배경화면으로 쓸 수 없어 제외됩니다.");
            }

            var (width, height) = _wallpaper.GetLargestMonitorSize();
            int longestSide = Math.Max(width, height);

            Directory.CreateDirectory(_cacheDirectory);

            var items = new List<PhotoItem>(picked.Count);
            for (int i = 0; i < picked.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PickedPhoto photo = picked[i];
                progress?.Report($"사진 내려받는 중... ({i + 1}/{picked.Count})");

                try
                {
                    // Access tokens last an hour; a large pick can outlive one, so refresh per item.
                    // The call is cached and only hits the network when the token has actually aged out.
                    accessToken = await _oauth.GetAccessTokenAsync(_client, cancellationToken)
                        .ConfigureAwait(false);

                    byte[] bytes = await _picker
                        .DownloadAsync(accessToken, photo, longestSide, cancellationToken)
                        .ConfigureAwait(false);

                    string path = Path.Combine(_cacheDirectory, BuildFileName(photo));
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

                    items.Add(new PhotoItem
                    {
                        Id = photo.Id,
                        FilePath = path,
                        FileName = photo.FileName,
                        CreatedAt = photo.CreateTime,
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // One bad photo should not abandon the whole selection.
                }
            }

            if (items.Count == 0)
            {
                throw new PickerApiException("사진을 하나도 내려받지 못했습니다. 네트워크 상태를 확인해 주세요.");
            }

            LastDownloadedCount = items.Count;
            progress?.Report($"{items.Count}장을 준비했습니다.");
            return items;
        }
        finally
        {
            await _picker.DeleteSessionAsync(accessToken, session.Id, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Filenames are derived from the Google media item id, which is stable and unique, so a repeat
    /// pick of the same photo overwrites rather than duplicating.
    /// </summary>
    private static string BuildFileName(PickedPhoto photo)
    {
        string? extension = Path.GetExtension(photo.FileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = photo.MimeType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg",
            };
        }

        // Media item ids can contain characters that are illegal in a path.
        string safeId = string.Concat(photo.Id.Where(char.IsLetterOrDigit));
        if (safeId.Length > 64)
        {
            safeId = safeId[^64..];
        }

        return safeId + extension;
    }

    private static void OpenPicker(string pickerUri)
    {
        if (string.IsNullOrEmpty(pickerUri))
        {
            throw new PickerApiException("Picker 주소를 받지 못했습니다.");
        }

        Process.Start(new ProcessStartInfo(pickerUri) { UseShellExecute = true });
    }
}
