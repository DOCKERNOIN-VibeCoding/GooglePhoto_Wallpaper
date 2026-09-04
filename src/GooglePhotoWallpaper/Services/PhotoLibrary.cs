using System.IO;
using System.Text.Json;
using GooglePhotoWallpaper.Models;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// The set of pictures currently in rotation, and where they sit on disk.
///
/// Persisted so a restart resumes the same rotation. That matters for the Google Photos source in
/// particular: its download links expire after an hour, so without a local copy the app would have
/// to ask the user to pick again every session.
/// </summary>
public sealed class PhotoLibrary
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _manifestPath;
    private readonly object _gate = new();
    private List<PhotoItem> _photos = [];

    public PhotoLibrary(string? manifestPath = null) => _manifestPath = manifestPath ?? AppPaths.ManifestFile;

    public IReadOnlyList<PhotoItem> Photos
    {
        get { lock (_gate) { return _photos.ToArray(); } }
    }

    public int Count
    {
        get { lock (_gate) { return _photos.Count; } }
    }

    /// <summary>Reads the manifest and drops entries whose file has since gone missing.</summary>
    public IReadOnlyList<PhotoItem> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_manifestPath))
            {
                _photos = [];
                return [];
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<List<PhotoItem>>(
                    File.ReadAllText(_manifestPath), Options) ?? [];
                _photos = loaded.Where(p => File.Exists(p.FilePath)).ToList();
            }
            catch (Exception)
            {
                _photos = [];
            }

            return _photos.ToArray();
        }
    }

    public void Replace(IEnumerable<PhotoItem> photos)
    {
        lock (_gate)
        {
            _photos = photos.ToList();
            Save();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        string temp = _manifestPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_photos, Options));
        File.Move(temp, _manifestPath, overwrite: true);
    }

    /// <summary>
    /// Deletes cached files that are no longer referenced. Called after a fresh pick so the cache
    /// does not grow without bound.
    /// </summary>
    public void PruneCache(string cacheDirectory)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        HashSet<string> keep;
        lock (_gate)
        {
            keep = _photos
                .Select(p => Path.GetFullPath(p.FilePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (string file in Directory.EnumerateFiles(cacheDirectory))
        {
            if (keep.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Most likely still set as the current wallpaper. It will be cleaned up next time.
            }
        }
    }
}
