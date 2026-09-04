using GooglePhotoWallpaper.Services;

namespace GooglePhotoWallpaper.Tests;

/// <summary>
/// Placement maths for the padded fit modes: where the photo sits on the monitor, and whether the
/// leftover gaps are worth composing a new image for.
/// </summary>
internal static class FitGeometryTests
{
    public static void Run()
    {
        PortraitOnLandscapeLeavesSideGaps();
        MatchingAspectNeedsNoPadding();
        NearlyMatchingAspectNeedsNoPadding();
        LandscapeOnPortraitLeavesTopAndBottomGaps();
        ContainNeverExceedsTheMonitor();
        CoverFillsTheMonitorAndOverhangs();
        RejectsNonsenseSizes();
    }

    /// <summary>The case that prompted the feature: a phone photo on a widescreen monitor.</summary>
    private static void PortraitOnLandscapeLeavesSideGaps()
    {
        FitLayout layout = FitGeometry.Contain(1080, 1920, 1920, 1080);

        Assert.Equal("photo uses the full screen height", 1080, layout.Height);
        Assert.Equal("width follows the photo's aspect", 608, layout.Width);
        Assert.Equal("photo is centred horizontally", 656, layout.OffsetX);
        Assert.Equal("no vertical offset", 0, layout.OffsetY);
        Assert.True("the gaps are worth filling", layout.NeedsPadding);
    }

    private static void MatchingAspectNeedsNoPadding()
    {
        FitLayout layout = FitGeometry.Contain(1920, 1080, 1920, 1080);

        Assert.Equal("fills the screen exactly", 1920, layout.Width);
        Assert.True("nothing to pad", !layout.NeedsPadding);
    }

    /// <summary>
    /// 16:10 on 16:9 leaves a bar of a few pixels. Composing a whole new image for that would be
    /// wasted work, so it passes through untouched.
    /// </summary>
    private static void NearlyMatchingAspectNeedsNoPadding()
    {
        FitLayout layout = FitGeometry.Contain(1920, 1090, 1920, 1080);

        Assert.True("a hairline gap is left alone", !layout.NeedsPadding);
    }

    private static void LandscapeOnPortraitLeavesTopAndBottomGaps()
    {
        FitLayout layout = FitGeometry.Contain(1920, 1080, 1080, 1920);

        Assert.Equal("photo uses the full screen width", 1080, layout.Width);
        Assert.Equal("height follows the photo's aspect", 608, layout.Height);
        Assert.Equal("photo is centred vertically", 656, layout.OffsetY);
        Assert.True("the gaps are worth filling", layout.NeedsPadding);
    }

    /// <summary>Rounding must not push the photo one pixel past the edge of the buffer.</summary>
    private static void ContainNeverExceedsTheMonitor()
    {
        foreach ((int w, int h) in new[] { (1001, 999), (3, 7), (4001, 2999), (1920, 1079) })
        {
            FitLayout layout = FitGeometry.Contain(w, h, 1920, 1080);

            Assert.True($"contained width fits for {w}x{h}", layout.Width <= 1920, $"{layout.Width}");
            Assert.True($"contained height fits for {w}x{h}", layout.Height <= 1080, $"{layout.Height}");
            Assert.True($"offsets stay positive for {w}x{h}",
                layout.OffsetX >= 0 && layout.OffsetY >= 0);
        }
    }

    private static void CoverFillsTheMonitorAndOverhangs()
    {
        FitLayout layout = FitGeometry.Cover(1080, 1920, 1920, 1080);

        Assert.True("covers the full width", layout.Width >= 1920, $"{layout.Width}");
        Assert.True("covers the full height", layout.Height >= 1080, $"{layout.Height}");
        Assert.True("overhang is split across both edges", layout.OffsetY <= 0, $"{layout.OffsetY}");
    }

    /// <summary>A corrupt or unreadable image reports zero dimensions; that must not divide by zero.</summary>
    private static void RejectsNonsenseSizes()
    {
        FitLayout layout = FitGeometry.Contain(0, 0, 1920, 1080);

        Assert.True("zero-sized photo needs no padding", !layout.NeedsPadding);
        Assert.Equal("falls back to the monitor size", 1920, layout.Width);
    }
}
