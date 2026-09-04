using System.Net;
using System.Net.Http;
using GooglePhotoWallpaper.Models;
using GooglePhotoWallpaper.Services.Sources;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Wires the app together. Hand-rolled rather than a container: there are six objects and one
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

        SettingsStore = new SettingsStore();
        Settings = SettingsStore.Load();

        Wallpaper = new WallpaperService();
        Library = new PhotoLibrary();
        Engine = new RotationEngine();
        Composer = new WallpaperComposer();

        Rotator = new WallpaperRotator(
            Wallpaper, Composer, Engine, Library, SettingsStore, Settings);
    }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    public WallpaperService Wallpaper { get; }

    public PhotoLibrary Library { get; }

    public RotationEngine Engine { get; }

    public WallpaperComposer Composer { get; }

    public WallpaperRotator Rotator { get; }

    /// <summary>Builds the source described by the current settings.</summary>
    public IPhotoSource? CreateSource()
    {
        if (Settings.Source == PhotoSourceKind.LocalFolder)
        {
            return string.IsNullOrWhiteSpace(Settings.LocalFolderPath)
                ? null
                : new LocalFolderSource(Settings.LocalFolderPath, Settings.LocalFolderRecursive);
        }

        return string.IsNullOrWhiteSpace(Settings.SharedAlbumUrl)
            ? null
            : new SharedAlbumSource(
                _http, Wallpaper, Settings.SharedAlbumUrl, Settings.AlbumSyncInterval);
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
