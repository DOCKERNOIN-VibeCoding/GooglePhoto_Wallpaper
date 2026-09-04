using System.Text.RegularExpressions;

namespace GooglePhotoWallpaper.Services.Sources;

public readonly record struct AlbumEntry(string MediaId, string BaseUrl, int Width, int Height);

/// <summary>
/// Reads the photo list out of a Google Photos share page.
///
/// This is the one part of the app that depends on a shape Google never promised to keep, so it
/// lives on its own with no dependencies: when the page format changes, the fix is here and the
/// tests that prove it are offline and instant.
///
/// The page embeds each photo as
/// <c>["&lt;mediaId&gt;",["&lt;baseUrl&gt;",&lt;width&gt;,&lt;height&gt;,...]]</c>.
/// </summary>
public static class SharedAlbumParser
{
    /// <summary>
    /// Anchoring on the AF1Qip media id keeps profile avatars and UI artwork out of the results -
    /// those sit under different googleusercontent paths and carry no media id.
    /// </summary>
    private static readonly Regex EntryPattern = new(
        """\["(AF1Qip[A-Za-z0-9_-]+)",\s*\["(https://lh3\.googleusercontent\.com/pw/[A-Za-z0-9_-]+)",\s*(\d+),\s*(\d+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Photos in album order. Google repeats an entry when the same media appears more than once in
    /// the payload, so the first occurrence wins and later ones are dropped.
    /// </summary>
    public static IReadOnlyList<AlbumEntry> Parse(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<AlbumEntry>();

        foreach (Match match in EntryPattern.Matches(html))
        {
            string mediaId = match.Groups[1].Value;
            if (!seen.Add(mediaId))
            {
                continue;
            }

            _ = int.TryParse(match.Groups[3].Value, out int width);
            _ = int.TryParse(match.Groups[4].Value, out int height);

            entries.Add(new AlbumEntry(mediaId, match.Groups[2].Value, width, height));
        }

        return entries;
    }

    /// <summary>
    /// Builds the download URL. "=wN-hN" bounds the image inside an NxN box, so the longer edge
    /// lands on N whichever way round the photo is - a portrait shot still comes back tall enough to
    /// fill a landscape monitor. Asking for more than the original holds just makes Google upscale,
    /// so the request is capped at the real size.
    /// </summary>
    public static string BuildDownloadUrl(AlbumEntry entry, int longestSide)
    {
        int original = Math.Max(entry.Width, entry.Height);
        int target = original > 0 ? Math.Min(longestSide, original) : longestSide;

        return $"{entry.BaseUrl}=w{target}-h{target}";
    }
}
