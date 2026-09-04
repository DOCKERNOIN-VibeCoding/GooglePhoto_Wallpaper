using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GooglePhotoWallpaper.Interop;

namespace GooglePhotoWallpaper.Services.Google;

public sealed class StoredToken
{
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("account_email")]
    public string? AccountEmail { get; set; }

    [JsonIgnore]
    public bool AccessTokenUsable =>
        !string.IsNullOrEmpty(AccessToken) && DateTimeOffset.UtcNow < ExpiresAt.AddMinutes(-2);
}

/// <summary>
/// Keeps the user's Google tokens on disk, encrypted with DPAPI so only this Windows user account
/// can read them back. Tokens never appear in settings.json, logs, or anywhere else.
/// </summary>
public sealed class TokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GooglePhotoWallpaper.v1.token");

    private readonly string _path;

    public TokenStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "token.dat");
    }

    public bool HasToken => File.Exists(_path);

    public StoredToken? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            byte[] plain = DataProtection.Unprotect(File.ReadAllBytes(_path), Entropy);
            return JsonSerializer.Deserialize<StoredToken>(plain);
        }
        catch (Exception)
        {
            // Corrupt, or written by a different Windows user. Force a fresh sign-in.
            return null;
        }
    }

    public void Save(StoredToken token)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(token);
        byte[] encrypted = DataProtection.Protect(plain, Entropy);
        File.WriteAllBytes(_path, encrypted);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
