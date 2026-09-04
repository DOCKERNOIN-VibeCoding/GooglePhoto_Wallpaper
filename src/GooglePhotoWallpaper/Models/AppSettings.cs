using System.Text.Json.Serialization;

namespace GooglePhotoWallpaper.Models;

public enum PhotoSourceKind
{
    GooglePhotos = 0,
    LocalFolder = 1,
}

public enum OAuthCredentialMode
{
    /// <summary>The user registers their own Google Cloud OAuth client. Nothing ships with the app.</summary>
    UserProvided = 0,

    /// <summary>A client id compiled into the build. Only usable once the app passes Google OAuth verification.</summary>
    Bundled = 1,
}

public sealed class AppSettings
{
    /// <summary>Minutes between wallpaper changes.</summary>
    public int IntervalMinutes { get; set; } = 30;

    public PhotoSourceKind Source { get; set; } = PhotoSourceKind.GooglePhotos;

    public string? LocalFolderPath { get; set; }

    /// <summary>Recurse into subfolders when <see cref="Source"/> is <see cref="PhotoSourceKind.LocalFolder"/>.</summary>
    public bool LocalFolderRecursive { get; set; } = true;

    public OAuthCredentialMode CredentialMode { get; set; } = OAuthCredentialMode.UserProvided;

    /// <summary>Give every monitor the same picture instead of the staggered sequence.</summary>
    public bool MirrorAllMonitors { get; set; }

    public bool Shuffle { get; set; }

    /// <summary>How Windows fits pictures to the screen. Windows applies one setting to every monitor.</summary>
    public Interop.DesktopWallpaperPosition Position { get; set; } = Interop.DesktopWallpaperPosition.Fill;

    public bool StartWithWindows { get; set; }

    /// <summary>Advance the rotation as soon as the app starts, rather than waiting a full interval.</summary>
    public bool ChangeOnStartup { get; set; } = true;

    /// <summary>Label shown in the UI for whatever the user picked last, e.g. an album name they typed.</summary>
    public string? SelectionLabel { get; set; }

    /// <summary>Position in the photo list. Monitor <c>i</c> shows <c>photos[(Offset + i) % Count]</c>.</summary>
    public int RotationOffset { get; set; }

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, 1, 60 * 24));
}
