using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GooglePhotoWallpaper.Services.Google;

public sealed record PickingSession(
    string Id,
    string PickerUri,
    TimeSpan PollInterval,
    TimeSpan TimeoutIn,
    bool MediaItemsSet);

public sealed record PickedPhoto(
    string Id,
    string BaseUrl,
    string? FileName,
    string? MimeType,
    int Width,
    int Height,
    DateTimeOffset? CreateTime);

public sealed class PickerApiException : Exception
{
    public PickerApiException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Wraps the Google Photos Picker API.
///
/// Flow: create a session, send the user to <c>pickerUri</c> in a browser, poll until they press
/// Done, then list what they picked. Google hands back short-lived <c>baseUrl</c> values rather than
/// durable links, so the bytes must be downloaded while the session is still alive.
/// </summary>
public sealed class PickerApiClient
{
    private const string BaseAddress = "https://photospicker.googleapis.com/v1";

    /// <summary>Google's own ceiling on a single picking session.</summary>
    public const int MaxItemsPerSession = 2000;

    private readonly HttpClient _http;

    public PickerApiClient(HttpClient http) => _http = http;

    public async Task<PickingSession> CreateSessionAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseAddress}/sessions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        JsonElement root = await SendAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        return ParseSession(root);
    }

    public async Task<PickingSession> GetSessionAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseAddress}/sessions/{Uri.EscapeDataString(sessionId)}");
        JsonElement root = await SendAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        return ParseSession(root);
    }

    public async Task DeleteSessionAsync(string accessToken, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, $"{BaseAddress}/sessions/{Uri.EscapeDataString(sessionId)}");
            await SendAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sessions expire on their own; failing to clean up early is not worth surfacing.
        }
    }

    /// <summary>
    /// Polls until the user finishes picking. <paramref name="onWaiting"/> reports the time left so
    /// the UI can show a countdown.
    /// </summary>
    public async Task<PickingSession> WaitForPickAsync(
        string accessToken,
        PickingSession session,
        Action<TimeSpan>? onWaiting,
        CancellationToken cancellationToken)
    {
        PickingSession current = session;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + current.TimeoutIn;

        while (!current.MediaItemsSet)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new PickerApiException("사진 선택 시간이 초과되었습니다. 다시 시도해 주세요.");
            }

            onWaiting?.Invoke(deadline - DateTimeOffset.UtcNow);

            TimeSpan wait = current.PollInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(3)
                : current.PollInterval;
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

            current = await GetSessionAsync(accessToken, current.Id, cancellationToken).ConfigureAwait(false);
        }

        return current;
    }

    /// <summary>Lists everything the user picked, following pagination to the end.</summary>
    public async Task<IReadOnlyList<PickedPhoto>> ListPickedPhotosAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken)
    {
        var photos = new List<PickedPhoto>();
        string? pageToken = null;

        do
        {
            var url = new StringBuilder($"{BaseAddress}/mediaItems");
            url.Append("?sessionId=").Append(Uri.EscapeDataString(sessionId));
            url.Append("&pageSize=100");
            if (!string.IsNullOrEmpty(pageToken))
            {
                url.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
            JsonElement root = await SendAsync(request, accessToken, cancellationToken).ConfigureAwait(false);

            if (root.TryGetProperty("mediaItems", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (ParsePhoto(item) is { } photo)
                    {
                        photos.Add(photo);
                    }
                }
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return photos;
    }

    /// <summary>
    /// Downloads one picked photo. The base URL is only valid for about an hour and always needs the
    /// bearer token, so this has to happen while the session is fresh.
    /// </summary>
    public async Task<byte[]> DownloadAsync(
        string accessToken, PickedPhoto photo, int longestSide, CancellationToken cancellationToken)
    {
        // "=wN-hN" bounds the image inside an NxN box, so the longer edge lands on N whichever way
        // round the photo is. That keeps portrait shots big enough to fill a landscape monitor.
        string url = $"{photo.BaseUrl}=w{longestSide}-h{longestSide}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new PickerApiException(
                $"사진을 내려받지 못했습니다 ({(int)response.StatusCode}). " +
                "선택 후 1시간이 지나면 링크가 만료되므로 사진을 다시 선택해 주세요.");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendAsync(
        HttpRequestMessage request, string accessToken, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new PickerApiException($"Picker API 오류 ({(int)response.StatusCode}): {ReadError(body)}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static PickingSession ParseSession(JsonElement root)
    {
        string? id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            throw new PickerApiException("Picker 세션 응답에 id가 없습니다.");
        }

        TimeSpan pollInterval = TimeSpan.FromSeconds(3);
        TimeSpan timeoutIn = TimeSpan.FromMinutes(30);

        if (root.TryGetProperty("pollingConfig", out var polling))
        {
            pollInterval = ReadDuration(polling, "pollInterval") ?? pollInterval;
            timeoutIn = ReadDuration(polling, "timeoutIn") ?? timeoutIn;
        }

        return new PickingSession(
            id,
            root.TryGetProperty("pickerUri", out var uri) ? uri.GetString() ?? string.Empty : string.Empty,
            pollInterval,
            timeoutIn,
            root.TryGetProperty("mediaItemsSet", out var set) && set.ValueKind == JsonValueKind.True);
    }

    private static PickedPhoto? ParsePhoto(JsonElement item)
    {
        if (!item.TryGetProperty("mediaFile", out var mediaFile))
        {
            return null;
        }

        string? baseUrl = mediaFile.TryGetProperty("baseUrl", out var b) ? b.GetString() : null;
        string? id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        // Videos cannot be a wallpaper, so drop anything that is not a photo.
        string type = item.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        if (!string.IsNullOrEmpty(type) && !type.Equals("PHOTO", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int width = 0;
        int height = 0;
        if (mediaFile.TryGetProperty("mediaFileMetadata", out var meta))
        {
            width = ReadInt(meta, "width");
            height = ReadInt(meta, "height");
        }

        DateTimeOffset? createTime = null;
        if (item.TryGetProperty("createTime", out var ct) &&
            ct.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(ct.GetString(), out var parsed))
        {
            createTime = parsed;
        }

        return new PickedPhoto(
            id,
            baseUrl,
            mediaFile.TryGetProperty("filename", out var f) ? f.GetString() : null,
            mediaFile.TryGetProperty("mimeType", out var m) ? m.GetString() : null,
            width,
            height,
            createTime);
    }

    /// <summary>Protobuf durations arrive as strings like "5s" or "1800.5s".</summary>
    private static TimeSpan? ReadDuration(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = element.GetString();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return double.TryParse(
            raw.TrimEnd('s', 'S'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    /// <summary>Numeric fields may be sent as JSON numbers or as strings (int64 encoding).</summary>
    private static int ReadInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return 0;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out int n) ? n : 0,
            JsonValueKind.String => int.TryParse(element.GetString(), out int s) ? s : 0,
            _ => 0,
        };
    }

    private static string ReadError(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? body;
            }
        }
        catch (Exception)
        {
            // Fall through to the raw body.
        }

        return body.Length > 300 ? body[..300] : body;
    }
}
