namespace GooglePhotoWallpaper.Models;

/// <summary>One picture available for rotation, already present on disk.</summary>
public sealed class PhotoItem
{
    /// <summary>Stable id. The Google Photos media item id, or the full path for local files.</summary>
    public required string Id { get; init; }

    /// <summary>Absolute path to the cached file.</summary>
    public required string FilePath { get; init; }

    public string? FileName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
