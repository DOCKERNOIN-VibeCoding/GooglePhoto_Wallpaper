namespace GooglePhotoWallpaper.Services;

/// <summary>Where a photo lands inside a monitor, and how big the leftover gaps are.</summary>
public readonly record struct FitLayout(int Width, int Height, int OffsetX, int OffsetY, double Coverage)
{
    /// <summary>
    /// True when the gaps are wide enough to be worth filling. A 16:9 photo on a 16:9 monitor leaves
    /// a hairline at most, and composing a whole new image for that would be wasted work.
    /// </summary>
    public bool NeedsPadding => Coverage < 0.985;
}

/// <summary>
/// Fitting maths, kept apart from imaging and Windows so it can be checked offline.
/// </summary>
public static class FitGeometry
{
    /// <summary>
    /// Largest centred rectangle with the photo's aspect ratio that fits inside the monitor - the
    /// whole photo is visible and the gaps are what padding fills.
    /// </summary>
    public static FitLayout Contain(int photoWidth, int photoHeight, int monitorWidth, int monitorHeight)
    {
        if (photoWidth <= 0 || photoHeight <= 0 || monitorWidth <= 0 || monitorHeight <= 0)
        {
            return new FitLayout(monitorWidth, monitorHeight, 0, 0, 1.0);
        }

        double scale = Math.Min(
            (double)monitorWidth / photoWidth,
            (double)monitorHeight / photoHeight);

        int width = Math.Max(1, (int)Math.Round(photoWidth * scale));
        int height = Math.Max(1, (int)Math.Round(photoHeight * scale));

        // Clamp: rounding can push a dimension one pixel past the monitor.
        width = Math.Min(width, monitorWidth);
        height = Math.Min(height, monitorHeight);

        double coverage = (double)width * height / ((double)monitorWidth * monitorHeight);

        return new FitLayout(
            width,
            height,
            (monitorWidth - width) / 2,
            (monitorHeight - height) / 2,
            coverage);
    }

    /// <summary>
    /// Smallest rectangle with the photo's aspect ratio that covers the whole monitor. Used for the
    /// blurred backdrop, where overflow is cropped away rather than shown.
    /// </summary>
    public static FitLayout Cover(int photoWidth, int photoHeight, int monitorWidth, int monitorHeight)
    {
        if (photoWidth <= 0 || photoHeight <= 0 || monitorWidth <= 0 || monitorHeight <= 0)
        {
            return new FitLayout(monitorWidth, monitorHeight, 0, 0, 1.0);
        }

        double scale = Math.Max(
            (double)monitorWidth / photoWidth,
            (double)monitorHeight / photoHeight);

        int width = Math.Max(monitorWidth, (int)Math.Round(photoWidth * scale));
        int height = Math.Max(monitorHeight, (int)Math.Round(photoHeight * scale));

        // Negative offsets: the overflow hangs off each edge equally.
        return new FitLayout(
            width,
            height,
            (monitorWidth - width) / 2,
            (monitorHeight - height) / 2,
            1.0);
    }
}
