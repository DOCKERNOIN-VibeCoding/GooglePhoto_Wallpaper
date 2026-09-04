using System.IO;
using System.Net.Http;
using GooglePhotoWallpaper.Models;

namespace GooglePhotoWallpaper.Services.Sources;

public sealed class SharedAlbumException : Exception
{
    public SharedAlbumException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Follows a Google Photos album that the user has shared by link.
///
/// This is the only route left that actually keeps up with an album: add a photo in Google Photos
/// and it turns up here on the next refresh. Every official API path is closed to a desktop app -
/// photoslibrary.readonly was withdrawn on 2025-03-31, the Library API now only sees media the app
/// itself uploaded, the sharing scopes went at the same time, and the Ambient API that powers
/// Chromecast slideshows needs Google partner approval.
///
/// So this reads the album's own public share page. The page embeds the photo list as
/// <c>["&lt;mediaId&gt;", ["&lt;baseUrl&gt;", width, height, ...]]</c>, and those base URLs serve
/// image bytes with no authentication - the owner made the link public. Nothing here bypasses an
/// access control; it reads exactly what anyone with the link can see.
///
/// The trade-off is that this is not a supported interface. Google can change the page shape and
/// break it, which is why the failure messages point at the album link rather than looking like a
/// crash, and why the local folder source stays available as a stable fallback.
/// </summary>
public sealed class SharedAlbumSource : IPhotoSource
{
    /// <summary>
    /// Default floor on how often the album page is re-fetched. Google serves no ETag and marks the
    /// page no-store, so every check is a full fetch - about 209 KB compressed. At 15 minutes that
    /// is roughly 20 MB a day; at one minute it would be 300 MB, which is why this is a setting
    /// rather than tied to the rotation interval.
    /// </summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(15);

    /// <summary>How many photos to fetch at once on a first sync.</summary>
    private const int MaxConcurrentDownloads = 6;

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly WallpaperService _wallpaper;
    private readonly string _albumUrl;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _refreshInterval;

    private readonly object _gate = new();
    private IReadOnlyList<PhotoItem>? _lastResult;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    public SharedAlbumSource(
        HttpClient http,
        WallpaperService wallpaper,
        string albumUrl,
        TimeSpan? refreshInterval = null,
        string? cacheDirectory = null)
    {
        _http = http;
        _wallpaper = wallpaper;
        _albumUrl = albumUrl;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
        _cacheDirectory = cacheDirectory ?? AppPaths.CacheDirectory;
    }

    public string DisplayName => "Google Photos 공유 앨범";

    /// <summary>True - this is the whole point. The timer can refresh it with nobody watching.</summary>
    public bool SupportsSilentRefresh => true;

    /// <summary>Photos added since the last refresh, from the most recent run.</summary>
    public int LastNewCount { get; private set; }

    public async Task<IReadOnlyList<PhotoItem>> RefreshAsync(
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_albumUrl))
        {
            throw new SharedAlbumException("공유 앨범 링크가 설정되어 있지 않습니다.");
        }

        lock (_gate)
        {
            if (_lastResult is not null && DateTimeOffset.UtcNow - _lastFetch < _refreshInterval)
            {
                return _lastResult;
            }
        }

        progress?.Report("공유 앨범을 읽는 중...");
        string html = await FetchAlbumPageAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AlbumEntry> entries = SharedAlbumParser.Parse(html);
        if (entries.Count == 0)
        {
            throw new SharedAlbumException(
                "앨범에서 사진을 찾지 못했습니다. 링크가 '링크가 있는 모든 사용자'로 공개된 공유 앨범인지 확인해 주세요.");
        }

        Directory.CreateDirectory(_cacheDirectory);

        var (width, height) = _wallpaper.GetLargestMonitorSize();
        int longestSide = Math.Max(width, height);

        // Photos already on disk cost nothing, so only genuinely new ones are fetched.
        var missing = new List<(int Index, AlbumEntry Entry, string Path)>();
        var paths = new string[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            string path = Path.Combine(_cacheDirectory, entries[i].MediaId + ".jpg");
            paths[i] = path;

            if (!IsUsableFile(path))
            {
                missing.Add((i, entries[i], path));
            }
        }

        int downloaded = 0;
        var failed = new bool[entries.Count];

        if (missing.Count > 0)
        {
            // Sequentially, a first sync of 88 photos took two minutes - almost all of it waiting on
            // round trips. A handful of concurrent downloads cuts that sharply while staying polite
            // to Google's servers.
            using var slots = new SemaphoreSlim(MaxConcurrentDownloads);

            await Task.WhenAll(missing.Select(async item =>
            {
                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await DownloadAsync(item.Entry, item.Path, longestSide, cancellationToken)
                        .ConfigureAwait(false);

                    int done = Interlocked.Increment(ref downloaded);
                    progress?.Report($"새 사진 내려받는 중... ({done}/{missing.Count})");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // A single unreachable photo should not sink the whole album.
                    failed[item.Index] = true;
                }
                finally
                {
                    slots.Release();
                }
            })).ConfigureAwait(false);
        }

        var items = new List<PhotoItem>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            if (failed[i])
            {
                continue;
            }

            items.Add(new PhotoItem
            {
                Id = entries[i].MediaId,
                FilePath = paths[i],
                FileName = entries[i].MediaId + ".jpg",
            });
        }

        if (items.Count == 0)
        {
            throw new SharedAlbumException("사진을 하나도 내려받지 못했습니다. 네트워크 상태를 확인해 주세요.");
        }

        LastNewCount = downloaded;
        progress?.Report(downloaded > 0
            ? $"사진 {items.Count}장 (새 사진 {downloaded}장)"
            : $"사진 {items.Count}장 (변경 없음)");

        lock (_gate)
        {
            _lastResult = items;
            _lastFetch = DateTimeOffset.UtcNow;
        }

        return items;
    }

    private async Task<string> FetchAlbumPageAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _albumUrl);

        // Google serves a stripped page to unrecognised clients; the share page only carries the
        // photo payload for a normal browser.
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new SharedAlbumException(
                $"공유 앨범을 열지 못했습니다 ({(int)response.StatusCode}). 링크가 올바른지 확인해 주세요.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadAsync(
        AlbumEntry entry, string path, int longestSide, CancellationToken cancellationToken)
    {
        string url = SharedAlbumParser.BuildDownloadUrl(entry, longestSide);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // Write to a temporary name first: a half-written file left by a dropped connection would
        // otherwise be mistaken for a cached photo on the next run.
        string temp = path + ".part";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private static bool IsUsableFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

}
