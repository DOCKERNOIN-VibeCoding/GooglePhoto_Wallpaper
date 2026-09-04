using System.IO;
using GooglePhotoWallpaper.Models;

namespace GooglePhotoWallpaper.Services.Sources;

/// <summary>
/// Reads pictures straight off a folder.
///
/// This is the answer to the one thing the Picker API cannot do: follow a set of photos as it
/// changes. Point this at a folder that something else keeps in sync - Google Takeout, a Drive or
/// OneDrive folder, a phone backup - and new files are picked up on the next refresh with no
/// re-authorisation and no picking.
/// </summary>
public sealed class LocalFolderSource : IPhotoSource
{
    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    private readonly string _folder;
    private readonly bool _recursive;

    public LocalFolderSource(string folder, bool recursive)
    {
        _folder = folder;
        _recursive = recursive;
    }

    public string DisplayName => "로컬 폴더";

    public bool SupportsSilentRefresh => true;

    public Task<IReadOnlyList<PhotoItem>> RefreshAsync(
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder))
        {
            throw new DirectoryNotFoundException($"폴더를 찾을 수 없습니다: {_folder}");
        }

        progress?.Report("폴더를 읽는 중...");

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = _recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var items = Directory
            .EnumerateFiles(_folder, "*", options)
            .Where(path => SupportedExtensions.Contains(
                Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new PhotoItem
            {
                Id = path,
                FilePath = path,
                FileName = Path.GetFileName(path),
                CreatedAt = SafeCreationTime(path),
            })
            .ToList();

        progress?.Report($"{items.Count}장을 찾았습니다.");
        return Task.FromResult<IReadOnlyList<PhotoItem>>(items);
    }

    private static DateTimeOffset? SafeCreationTime(string path)
    {
        try
        {
            return File.GetCreationTime(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
