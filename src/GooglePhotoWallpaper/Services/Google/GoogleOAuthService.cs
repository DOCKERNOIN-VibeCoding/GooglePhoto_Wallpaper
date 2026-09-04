using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GooglePhotoWallpaper.Services.Google;

public sealed class GoogleAuthException : Exception
{
    public GoogleAuthException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    /// <summary>
    /// True when the only thing wrong is that there is no usable token, so signing in again fixes
    /// it. Callers use this to re-authenticate silently instead of showing the user an error.
    /// </summary>
    public bool RequiresSignIn { get; init; }
}

/// <summary>
/// Signs the user in with the OAuth 2.0 authorization-code flow for installed apps: PKCE plus a
/// loopback redirect. Credentials are only ever typed into Google's own pages; this app receives a
/// refresh token and nothing else.
/// </summary>
public sealed class GoogleOAuthService
{
    /// <summary>
    /// Read-only access to the media items the user picks in Google's own picker. This is the only
    /// Photos scope still open to general developers - photoslibrary.readonly was removed on
    /// 2025-03-31 and now returns 403 PERMISSION_DENIED.
    /// </summary>
    public const string PhotosPickerScope = "https://www.googleapis.com/auth/photospicker.mediaitems.readonly";

    private static readonly string[] Scopes = ["openid", "email", PhotosPickerScope];

    private readonly HttpClient _http;
    private readonly TokenStore _tokenStore;

    public GoogleOAuthService(HttpClient http, TokenStore tokenStore)
    {
        _http = http;
        _tokenStore = tokenStore;
    }

    /// <summary>Opens the browser for consent and stores the resulting refresh token.</summary>
    public async Task<StoredToken> SignInAsync(OAuthClientConfig client, CancellationToken cancellationToken)
    {
        using var listener = new LoopbackListener();

        string verifier = CreateCodeVerifier();
        string challenge = CreateCodeChallenge(verifier);
        string state = CreateCodeVerifier();

        var authUrl = new StringBuilder(client.AuthUri);
        authUrl.Append("?client_id=").Append(Uri.EscapeDataString(client.ClientId));
        authUrl.Append("&redirect_uri=").Append(Uri.EscapeDataString(listener.RedirectUri));
        authUrl.Append("&response_type=code");
        authUrl.Append("&scope=").Append(Uri.EscapeDataString(string.Join(' ', Scopes)));
        authUrl.Append("&code_challenge=").Append(challenge);
        authUrl.Append("&code_challenge_method=S256");
        authUrl.Append("&state=").Append(state);

        // offline + consent is what makes Google hand back a refresh token.
        authUrl.Append("&access_type=offline&prompt=consent");

        OpenBrowser(authUrl.ToString());

        Dictionary<string, string> callback =
            await listener.WaitForCallbackAsync(cancellationToken).ConfigureAwait(false);

        if (callback.TryGetValue("error", out string? error))
        {
            throw new GoogleAuthException($"Google 인증이 취소되었거나 거부되었습니다: {error}");
        }

        if (!callback.TryGetValue("state", out string? returnedState) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(returnedState), Encoding.UTF8.GetBytes(state)))
        {
            throw new GoogleAuthException("인증 응답의 state 값이 일치하지 않습니다. 다시 시도해 주세요.");
        }

        if (!callback.TryGetValue("code", out string? code))
        {
            throw new GoogleAuthException("인증 코드를 받지 못했습니다.");
        }

        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = listener.RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };

        if (!string.IsNullOrEmpty(client.ClientSecret))
        {
            form["client_secret"] = client.ClientSecret;
        }

        StoredToken token = await ExchangeAsync(client, form, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new GoogleAuthException(
                "refresh token을 받지 못했습니다. Google 계정 보안 설정에서 이 앱의 기존 권한을 삭제한 뒤 다시 시도해 주세요.");
        }

        _tokenStore.Save(token);
        return token;
    }

    /// <summary>Returns a usable access token, refreshing the cached one when it has expired.</summary>
    public async Task<string> GetAccessTokenAsync(OAuthClientConfig client, CancellationToken cancellationToken)
    {
        StoredToken token = _tokenStore.Load()
            ?? throw new GoogleAuthException("Google 계정이 연결되어 있지 않습니다.") { RequiresSignIn = true };

        if (token.AccessTokenUsable)
        {
            return token.AccessToken!;
        }

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new GoogleAuthException("저장된 refresh token이 없습니다.") { RequiresSignIn = true };
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["refresh_token"] = token.RefreshToken,
            ["grant_type"] = "refresh_token",
        };

        if (!string.IsNullOrEmpty(client.ClientSecret))
        {
            form["client_secret"] = client.ClientSecret;
        }

        StoredToken refreshed;
        try
        {
            refreshed = await ExchangeAsync(client, form, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleAuthException ex) when (ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
        {
            _tokenStore.Clear();
            throw new GoogleAuthException(
                "저장된 인증이 만료되었습니다.", ex) { RequiresSignIn = true };
        }

        // A refresh response does not repeat the refresh token; keep the one already stored.
        refreshed.RefreshToken ??= token.RefreshToken;
        refreshed.AccountEmail ??= token.AccountEmail;
        _tokenStore.Save(refreshed);

        return refreshed.AccessToken!;
    }

    /// <summary>
    /// Returns a usable access token, signing in again if the stored one is gone or has expired.
    ///
    /// This is what makes the 7-day refresh token lifetime of an unpublished OAuth client a
    /// non-event. Rotation never calls this - it reads cached files - so a dead token only surfaces
    /// when the user is picking photos, and picking already sends them to the browser. Rather than
    /// failing with "reconnect your account", the browser trip just happens to start with a login.
    ///
    /// Only call this from a user-initiated action: it can open a browser window.
    /// </summary>
    public async Task<string> EnsureAccessTokenAsync(
        OAuthClientConfig client, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAccessTokenAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleAuthException ex) when (ex.RequiresSignIn)
        {
            progress?.Report("Google 로그인이 필요합니다. 브라우저에서 로그인해 주세요...");
            await SignInAsync(client, cancellationToken).ConfigureAwait(false);
            return await GetAccessTokenAsync(client, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RevokeAsync(CancellationToken cancellationToken)
    {
        StoredToken? token = _tokenStore.Load();
        string? toRevoke = token?.RefreshToken ?? token?.AccessToken;

        if (!string.IsNullOrEmpty(toRevoke))
        {
            try
            {
                using var content = new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["token"] = toRevoke });
                await _http.PostAsync("https://oauth2.googleapis.com/revoke", content, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Server-side revoke is best effort; clearing the local copy is what matters.
            }
        }

        _tokenStore.Clear();
    }

    private async Task<StoredToken> ExchangeAsync(
        OAuthClientConfig client, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, client.TokenUri)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleAuthException($"토큰 요청 실패 ({(int)response.StatusCode}): {Summarize(body)}");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        var token = new StoredToken
        {
            AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null,
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600),
        };

        if (root.TryGetProperty("id_token", out var idToken) && idToken.GetString() is { } jwt)
        {
            token.AccountEmail = ReadEmailClaim(jwt);
        }

        return token;
    }

    /// <summary>
    /// Pulls the email out of the id_token payload. No signature check: the token came straight from
    /// Google's token endpoint over TLS, and it is only used as a display label.
    /// </summary>
    private static string? ReadEmailClaim(string jwt)
    {
        try
        {
            string[] parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

            using JsonDocument doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reduces an error body to its error code, so no token text reaches the UI or logs.</summary>
    private static string Summarize(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            string? error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            string? description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return string.Join(" - ", new[] { error, description }.Where(s => !string.IsNullOrEmpty(s)));
        }
        catch (Exception)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    private static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier)
        => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void OpenBrowser(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
