using System.IO;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Where the app keeps its settings, photo cache and log.
///
/// Portable by default: everything lives in a folder beside the .exe, so the program can be carried
/// on a USB stick with its settings and pictures, and deleting that folder removes every trace of
/// it. When the .exe sits somewhere unwritable - Program Files, a read-only share, a network path -
/// that would fail on first run, so the app falls back to the usual per-user location instead.
/// </summary>
public static class AppPaths
{
    private const string DataFolderName = "GooglePhotoWallpaper-Data";

    static AppPaths()
    {
        Root = ResolveRoot(out bool portable);
        IsPortable = portable;

        CacheDirectory = Path.Combine(Root, "cache");
        ComposedDirectory = Path.Combine(CacheDirectory, "fitted");
        SettingsFile = Path.Combine(Root, "settings.json");
        ManifestFile = Path.Combine(Root, "photos.json");
        LogFile = Path.Combine(Root, "app.log");
    }

    public static string Root { get; }

    /// <summary>True when data sits beside the .exe rather than under the user's profile.</summary>
    public static bool IsPortable { get; }

    /// <summary>Downloaded pictures. Wallpaper files must stay on disk for as long as they are set.</summary>
    public static string CacheDirectory { get; }

    /// <summary>
    /// Monitor-sized images built for the padded fit modes. A subdirectory of the cache, so the
    /// non-recursive prune that cleans downloaded photos leaves these alone.
    /// </summary>
    public static string ComposedDirectory { get; }

    public static string SettingsFile { get; }

    public static string ManifestFile { get; }

    public static string LogFile { get; }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ComposedDirectory);
    }

    private static string ResolveRoot(out bool portable)
    {
        // Environment.ProcessPath, not AppContext.BaseDirectory: a single-file build unpacks itself
        // into a temporary directory, and BaseDirectory would point at that instead of the .exe.
        string? exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);

        if (!string.IsNullOrEmpty(exeDirectory) && CanWrite(exeDirectory))
        {
            portable = true;
            return Path.Combine(exeDirectory, DataFolderName);
        }

        portable = false;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GooglePhotoWallpaper");
    }

    /// <summary>
    /// Probes by actually creating a file. Permission flags alone would not catch a read-only
    /// medium, and this runs once at startup.
    /// </summary>
    private static bool CanWrite(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
