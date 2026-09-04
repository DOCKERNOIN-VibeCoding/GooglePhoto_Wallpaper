using System.IO;
using System.Net;
using System.Net.Http;
using GooglePhotoWallpaper.Models;
using GooglePhotoWallpaper.Services.Google;
using GooglePhotoWallpaper.Services.Sources;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Wires the app together. Hand-rolled rather than a container: there are seven objects and one
/// lifetime.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly HttpClient _http;

    public AppServices()
    {
        AppPaths.EnsureCreated();

        // The shared-album page is 1.17 MB raw and 209 KB compressed, and it is polled on a timer,
        // so decompression is worth turning on explicitly.
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GooglePhotoWallpaper/1.0");

        SettingsStore = new SettingsStore();
        Settings = SettingsStore.Load();

        Wallpaper = new WallpaperService();
        Library = new PhotoLibrary();
        Engine = new RotationEngine();
        TokenStore = new TokenStore(AppPaths.Root);
        OAuth = new GoogleOAuthService(_http, TokenStore);
        Picker = new PickerApiClient(_http);

        Rotator = new WallpaperRotator(Wallpaper, Engine, Library, SettingsStore, Settings);
    }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    public WallpaperService Wallpaper { get; }

    public PhotoLibrary Library { get; }

    public RotationEngine Engine { get; }

    public TokenStore TokenStore { get; }

    public GoogleOAuthService OAuth { get; }

    public PickerApiClient Picker { get; }

    public WallpaperRotator Rotator { get; }

    public bool IsGoogleConnected => TokenStore.HasToken;

    public string? ConnectedAccount => TokenStore.Load()?.AccountEmail;

    /// <summary>True once an OAuth client is available, whether bundled or supplied by the user.</summary>
    public bool HasOAuthClient => ResolveOAuthClient() is not null;

    /// <summary>
    /// Finds the OAuth client to authenticate with.
    ///
    /// A client the user registered themselves always wins: if they went to the trouble of
    /// configuring one, that is the project whose quota and consent screen they expect to see.
    /// </summary>
    public OAuthClientConfig? ResolveOAuthClient()
    {
        if (Settings.CredentialMode == OAuthCredentialMode.Bundled && OAuthClientConfig.HasBundledClient)
        {
            return OAuthClientConfig.Bundled;
        }

        if (File.Exists(AppPaths.ClientSecretFile))
        {
            try
            {
                return OAuthClientConfig.FromClientSecretFile(AppPaths.ClientSecretFile);
            }
            catch (Exception)
            {
                return OAuthClientConfig.Bundled;
            }
        }

        return OAuthClientConfig.Bundled;
    }

    public OAuthClientConfig RequireOAuthClient()
        => ResolveOAuthClient()
           ?? throw new GoogleAuthException(
               "OAuth 클라이언트가 설정되어 있지 않습니다. 설정에서 client_secret.json을 등록해 주세요. " +
               "발급 방법은 docs/GOOGLE-CLOUD-SETUP.md를 참고하세요.");

    /// <summary>Copies the user's client_secret.json into the app folder so the original can move.</summary>
    public void ImportClientSecret(string sourcePath)
    {
        // Parse first: a bad file should be rejected before it replaces a working one.
        OAuthClientConfig.FromClientSecretFile(sourcePath);
        File.Copy(sourcePath, AppPaths.ClientSecretFile, overwrite: true);
    }

    public void ClearClientSecret()
    {
        if (File.Exists(AppPaths.ClientSecretFile))
        {
            File.Delete(AppPaths.ClientSecretFile);
        }
    }

    /// <summary>Builds the source described by the current settings.</summary>
    public IPhotoSource? CreateSource()
    {
        switch (Settings.Source)
        {
            case PhotoSourceKind.SharedAlbum:
                return string.IsNullOrWhiteSpace(Settings.SharedAlbumUrl)
                    ? null
                    : new SharedAlbumSource(
                        _http, Wallpaper, Settings.SharedAlbumUrl, Settings.AlbumSyncInterval);

            case PhotoSourceKind.LocalFolder:
                return string.IsNullOrWhiteSpace(Settings.LocalFolderPath)
                    ? null
                    : new LocalFolderSource(Settings.LocalFolderPath, Settings.LocalFolderRecursive);

            default:
                OAuthClientConfig? client = ResolveOAuthClient();
                return client is null
                    ? null
                    : new GooglePhotosPickerSource(OAuth, Picker, Wallpaper, client);
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
        Rotator.UpdateSettings(settings, CreateSource());
    }

    public void Dispose()
    {
        Rotator.Dispose();
        _http.Dispose();
    }
}
