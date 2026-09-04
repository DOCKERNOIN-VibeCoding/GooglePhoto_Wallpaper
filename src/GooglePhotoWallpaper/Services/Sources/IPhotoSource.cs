using GooglePhotoWallpaper.Models;

namespace GooglePhotoWallpaper.Services.Sources;

/// <summary>
/// Where the pictures come from.
///
/// Kept as an interface because Google's terms for reaching a Photos library differ by API, and the
/// one that would let an app follow an album on its own - the Ambient API - is gated behind partner
/// approval. If that approval ever lands, it plugs in here without touching rotation or wallpaper
/// code.
/// </summary>
public interface IPhotoSource
{
    string DisplayName { get; }

    /// <summary>
    /// False when refreshing needs the user present. The Google Photos picker requires a browser
    /// round trip, so it can only run from the settings window - never from the background timer.
    /// </summary>
    bool SupportsSilentRefresh { get; }

    /// <summary>Produces the current set of pictures, downloading them if the source is remote.</summary>
    Task<IReadOnlyList<PhotoItem>> RefreshAsync(
        IProgress<string>? progress, CancellationToken cancellationToken);
}
