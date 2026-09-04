using GooglePhotoWallpaper.Services.Sources;

namespace GooglePhotoWallpaper.Tests;

/// <summary>
/// Guards the one piece of the app that reads a format Google never promised to keep: the photo
/// list embedded in a shared album page.
///
/// The fixtures below reproduce the real payload shape rather than embedding a captured page, so
/// nothing here depends on a live album or exposes anyone's photos. When Google changes the format
/// and the album source stops finding photos, these are the tests to update alongside the regex.
/// </summary>
internal static class SharedAlbumParserTests
{
    /// <summary>Two photos in the nesting the share page actually uses.</summary>
    private const string TwoPhotos = """
        ["AF1QipAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",["https://lh3.googleusercontent.com/pw/AP1GczAAAA",2134,1200,null,null,null],
        ["AF1QipBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",["https://lh3.googleusercontent.com/pw/AP1GczBBBB",1280,720,null,null,null]
        """;

    public static void Run()
    {
        ReadsPhotosInOrder();
        SkipsRepeatedMediaItems();
        IgnoresAvatarsAndOtherImages();
        SurvivesUnexpectedInput();
        BuildsBoundedDownloadUrls();
    }

    private static void ReadsPhotosInOrder()
    {
        IReadOnlyList<AlbumEntry> entries = SharedAlbumParser.Parse(TwoPhotos);

        Assert.Equal("finds both photos", 2, entries.Count);
        Assert.Equal("keeps album order", "AF1QipAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", entries[0].MediaId);
        Assert.Equal("reads the base url", "https://lh3.googleusercontent.com/pw/AP1GczBBBB", entries[1].BaseUrl);
        Assert.Equal("reads the original width", 2134, entries[0].Width);
        Assert.Equal("reads the original height", 1200, entries[0].Height);
    }

    /// <summary>Google emits the same media item more than once in a single page.</summary>
    private static void SkipsRepeatedMediaItems()
    {
        IReadOnlyList<AlbumEntry> entries = SharedAlbumParser.Parse(TwoPhotos + TwoPhotos);

        Assert.Equal("a repeated entry is counted once", 2, entries.Count);
    }

    /// <summary>Profile pictures and UI artwork sit under other paths and carry no media id.</summary>
    private static void IgnoresAvatarsAndOtherImages()
    {
        const string WithNoise = """
            ["https://lh3.googleusercontent.com/a/AvatarImageHere",64,64],
            ["https://lh3.googleusercontent.com/ogw/SomeLogoHere",32,32],
            ["AF1QipAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",["https://lh3.googleusercontent.com/pw/AP1GczAAAA",800,600,null]
            """;

        IReadOnlyList<AlbumEntry> entries = SharedAlbumParser.Parse(WithNoise);

        Assert.Equal("only the real photo is returned", 1, entries.Count);
        Assert.True("the avatar was skipped", entries[0].BaseUrl.Contains("/pw/", StringComparison.Ordinal));
    }

    /// <summary>
    /// A changed page format or an error page must come back empty rather than throwing - the album
    /// source turns an empty result into a message about the share link.
    /// </summary>
    private static void SurvivesUnexpectedInput()
    {
        Assert.Equal("empty input yields nothing", 0, SharedAlbumParser.Parse(string.Empty).Count);
        Assert.Equal("unrelated html yields nothing", 0, SharedAlbumParser.Parse("<html>nope</html>").Count);
        Assert.Equal("a truncated entry is ignored", 0,
            SharedAlbumParser.Parse("""["AF1QipAAAA",["https://lh3.googleusercontent.com/pw/AP1Gcz""").Count);
    }

    private static void BuildsBoundedDownloadUrls()
    {
        var large = new AlbumEntry("id", "https://example.test/photo", 4000, 3000);
        var small = new AlbumEntry("id", "https://example.test/photo", 800, 600);
        var unknown = new AlbumEntry("id", "https://example.test/photo", 0, 0);

        Assert.Equal("caps the request at the monitor size",
            "https://example.test/photo=w1920-h1920",
            SharedAlbumParser.BuildDownloadUrl(large, 1920));

        // Asking for more than the original holds only makes Google upscale.
        Assert.Equal("never asks for more than the original",
            "https://example.test/photo=w800-h800",
            SharedAlbumParser.BuildDownloadUrl(small, 1920));

        Assert.Equal("falls back to the monitor size when dimensions are missing",
            "https://example.test/photo=w1920-h1920",
            SharedAlbumParser.BuildDownloadUrl(unknown, 1920));
    }
}
