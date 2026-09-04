using System.IO;
using System.Text.Json;

namespace GooglePhotoWallpaper.Services.Google;

/// <summary>
/// Identity of the *application* with Google - not the end user's account.
///
/// Every OAuth app needs one. Two ways to get it:
///   - UserProvided: each user registers their own Google Cloud OAuth client and points the app at
///     the downloaded client_secret.json. Nothing sensitive ships in the binary, so the build can be
///     redistributed freely and needs no Google verification.
///   - Bundled: an id compiled into the build. Convenient, but redistribution requires passing
///     Google's OAuth verification first (see docs/OAUTH-DISTRIBUTION.md).
///
/// A desktop client_secret is not a real secret - Google's installed-app flow assumes it is
/// extractable from the binary, which is why PKCE carries the actual security here.
/// </summary>
public sealed class OAuthClientConfig
{
    public required string ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string AuthUri { get; init; } = "https://accounts.google.com/o/oauth2/v2/auth";

    public string TokenUri { get; init; } = "https://oauth2.googleapis.com/token";

    public string? ProjectId { get; init; }

    /// <summary>
    /// Filled in at build time only if you have completed Google OAuth verification.
    /// Left empty in the public build on purpose.
    /// </summary>
    public const string BundledClientId = "";

    public const string BundledClientSecret = "";

    public static bool HasBundledClient => !string.IsNullOrWhiteSpace(BundledClientId);

    public static OAuthClientConfig? Bundled => HasBundledClient
        ? new OAuthClientConfig { ClientId = BundledClientId, ClientSecret = BundledClientSecret }
        : null;

    /// <summary>Reads a client_secret.json downloaded from the Google Cloud console.</summary>
    public static OAuthClientConfig FromClientSecretJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Desktop clients nest under "installed"; web clients under "web".
        if (!root.TryGetProperty("installed", out var section) &&
            !root.TryGetProperty("web", out section))
        {
            section = root;
        }

        string? clientId = GetString(section, "client_id");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidDataException(
                "client_id를 찾을 수 없습니다. Google Cloud 콘솔에서 받은 client_secret.json 파일이 맞는지 확인하세요.");
        }

        return new OAuthClientConfig
        {
            ClientId = clientId,
            ClientSecret = GetString(section, "client_secret"),
            ProjectId = GetString(section, "project_id"),
            AuthUri = GetString(section, "auth_uri") ?? "https://accounts.google.com/o/oauth2/v2/auth",
            TokenUri = GetString(section, "token_uri") ?? "https://oauth2.googleapis.com/token",
        };
    }

    public static OAuthClientConfig FromClientSecretFile(string path)
        => FromClientSecretJson(File.ReadAllText(path));

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
