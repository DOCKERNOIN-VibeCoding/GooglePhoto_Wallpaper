using System.Text.Json.Serialization;

namespace GooglePhotoWallpaper.Models;

public enum PhotoSourceKind
{
    /// <summary>Pick photos in Google's picker. A snapshot - it does not follow the album.</summary>
    GooglePhotos = 0,

    LocalFolder = 1,

    /// <summary>
    /// Follow a link-shared Google Photos album. The only option that picks up photos added to the
    /// album later, so it is the default.
    /// </summary>
    SharedAlbum = 2,
}

public enum MonitorAssignmentMode
{
    /// <summary>Each monitor sits one photo further along the list: A/B, then B/C, then C/D.</summary>
    Sequential = 0,

    /// <summary>Every monitor shows the same photo.</summary>
    Mirror = 1,

    /// <summary>Only one chosen monitor changes; the others keep whatever wallpaper they have.</summary>
    Single = 2,
}

/// <summary>
/// How a photo is fitted to a monitor.
///
/// The padded modes are not Windows wallpaper positions - Windows has no such mode - so the app
/// composes an image at the monitor's exact resolution and sets that instead.
/// </summary>
public enum PhotoFitMode
{
    /// <summary>Windows crops the photo to cover the screen. Fast, but cuts off the edges.</summary>
    Crop = 0,

    /// <summary>Whole photo, with the gaps filled by a blurred, zoomed copy of itself.</summary>
    BlurredPadding = 1,

    /// <summary>Whole photo, with plain black bars in the gaps.</summary>
    SolidPadding = 2,

    /// <summary>Windows shrinks the photo to fit, leaving the background colour visible.</summary>
    WindowsFit = 3,

    /// <summary>Windows stretches the photo, ignoring its aspect ratio.</summary>
    WindowsStretch = 4,

    /// <summary>Windows centres the photo at its original size.</summary>
    WindowsCenter = 5,

    /// <summary>Windows repeats the photo across the screen.</summary>
    WindowsTile = 6,

    /// <summary>Windows spans one photo across every monitor.</summary>
    WindowsSpan = 7,
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

    public PhotoSourceKind Source { get; set; } = PhotoSourceKind.SharedAlbum;

    /// <summary>Share link of the Google Photos album to follow, short or full form.</summary>
    public string? SharedAlbumUrl { get; set; }

    /// <summary>
    /// Minutes between checks of the shared album. Separate from <see cref="IntervalMinutes"/>
    /// because each check re-fetches the whole album page - roughly 209 KB, since Google sends no
    /// ETag - so 15 minutes costs about 20 MB a day and one minute would cost 300 MB.
    /// </summary>
    public int AlbumSyncMinutes { get; set; } = 15;

    public string? LocalFolderPath { get; set; }

    /// <summary>Recurse into subfolders when <see cref="Source"/> is <see cref="PhotoSourceKind.LocalFolder"/>.</summary>
    public bool LocalFolderRecursive { get; set; } = true;

    public OAuthCredentialMode CredentialMode { get; set; } = OAuthCredentialMode.UserProvided;

    public MonitorAssignmentMode MonitorMode { get; set; } = MonitorAssignmentMode.Sequential;

    /// <summary>
    /// Which monitor changes in <see cref="MonitorAssignmentMode.Single"/> mode, zero-based and in
    /// the order the settings window lists them. Clamped if that monitor is unplugged.
    /// </summary>
    public int TargetMonitorIndex { get; set; }

    public bool Shuffle { get; set; }

    public PhotoFitMode FitMode { get; set; } = PhotoFitMode.Crop;

    public bool StartWithWindows { get; set; }

    /// <summary>Advance the rotation as soon as the app starts, rather than waiting a full interval.</summary>
    public bool ChangeOnStartup { get; set; } = true;

    /// <summary>Label shown in the UI for whatever the user picked last, e.g. an album name they typed.</summary>
    public string? SelectionLabel { get; set; }

    /// <summary>Position in the photo list. Monitor <c>i</c> shows <c>photos[(Offset + i) % Count]</c>.</summary>
    public int RotationOffset { get; set; }

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, 1, 60 * 24));

    [JsonIgnore]
    public TimeSpan AlbumSyncInterval => TimeSpan.FromMinutes(Math.Clamp(AlbumSyncMinutes, 1, 60 * 24));

    /// <summary>True when the app has to build the image itself rather than let Windows place it.</summary>
    [JsonIgnore]
    public bool RequiresComposition =>
        FitMode is PhotoFitMode.BlurredPadding or PhotoFitMode.SolidPadding;

    /// <summary>
    /// The Windows placement to pair with the chosen fit. Composed images are already exactly the
    /// monitor's size, so they are set to Fill - any placement would leave them untouched.
    /// </summary>
    [JsonIgnore]
    public Interop.DesktopWallpaperPosition WindowsPosition => FitMode switch
    {
        PhotoFitMode.WindowsFit => Interop.DesktopWallpaperPosition.Fit,
        PhotoFitMode.WindowsStretch => Interop.DesktopWallpaperPosition.Stretch,
        PhotoFitMode.WindowsCenter => Interop.DesktopWallpaperPosition.Center,
        PhotoFitMode.WindowsTile => Interop.DesktopWallpaperPosition.Tile,
        PhotoFitMode.WindowsSpan => Interop.DesktopWallpaperPosition.Span,
        _ => Interop.DesktopWallpaperPosition.Fill,
    };
}
