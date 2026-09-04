using System.IO;

namespace GooglePhotoWallpaper.Services;

/// <summary>Everything the app writes lives under %LOCALAPPDATA%\GooglePhotoWallpaper.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GooglePhotoWallpaper");

    /// <summary>Downloaded pictures. Wallpaper files must stay on disk for as long as they are set.</summary>
    public static string CacheDirectory { get; } = Path.Combine(Root, "cache");

    /// <summary>
    /// Monitor-sized images built for the padded fit modes. A subdirectory of the cache, so the
    /// non-recursive prune that cleans downloaded photos leaves these alone.
    /// </summary>
    public static string ComposedDirectory { get; } = Path.Combine(CacheDirectory, "fitted");

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    public static string ManifestFile { get; } = Path.Combine(Root, "photos.json");

    public static string LogFile { get; } = Path.Combine(Root, "app.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ComposedDirectory);
    }
}
