using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GooglePhotoWallpaper.Models;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Builds a monitor-sized image for photos whose shape does not match the screen.
///
/// Windows offers no wallpaper mode that shows a whole portrait photo on a landscape monitor
/// without either cropping it or leaving flat bars, so the app draws the picture itself: the photo
/// centred at full height, and the gaps filled either with black or with a blurred, zoomed copy of
/// the same photo - the treatment Instagram and YouTube use for mismatched video.
///
/// Everything is done on raw pixel buffers rather than through WPF's visual layer, so composition
/// runs on the rotation timer's thread without touching the UI dispatcher, and results are cached to
/// disk so each photo is only ever composed once per monitor size.
/// </summary>
public sealed class WallpaperComposer
{
    /// <summary>
    /// Long edge of the backdrop before it is blurred and scaled up. Blurring a thumbnail and
    /// enlarging it is indistinguishable from blurring at full size, and is orders of magnitude
    /// cheaper.
    /// </summary>
    private const int BackdropSampleSize = 120;

    /// <summary>Box blur passes. Three approximates a Gaussian closely enough to look smooth.</summary>
    private const int BlurPasses = 3;

    private const int BlurRadius = 12;

    /// <summary>The backdrop is dimmed so the sharp photo in front of it stays dominant.</summary>
    private const double BackdropDimming = 0.55;

    private readonly string _directory;

    public WallpaperComposer(string? directory = null)
        => _directory = directory ?? AppPaths.ComposedDirectory;

    /// <summary>
    /// Path of the image to hand Windows. Returns the original photo untouched when the mode needs
    /// no composition, or when the photo already fills the monitor.
    /// </summary>
    public string Resolve(PhotoItem photo, int monitorWidth, int monitorHeight, PhotoFitMode mode)
    {
        if (mode is not (PhotoFitMode.BlurredPadding or PhotoFitMode.SolidPadding))
        {
            return photo.FilePath;
        }

        try
        {
            (int photoWidth, int photoHeight) = ReadDimensions(photo.FilePath);
            FitLayout layout = FitGeometry.Contain(photoWidth, photoHeight, monitorWidth, monitorHeight);

            if (!layout.NeedsPadding)
            {
                return photo.FilePath;
            }

            string target = Path.Combine(
                _directory,
                $"{SafeName(photo.Id)}_{monitorWidth}x{monitorHeight}_{(int)mode}.jpg");

            if (File.Exists(target) && new FileInfo(target).Length > 0)
            {
                return target;
            }

            Directory.CreateDirectory(_directory);
            Compose(photo.FilePath, layout, monitorWidth, monitorHeight, mode, target);
            return target;
        }
        catch (Exception)
        {
            // Composition is a nicety; a failure should still leave a wallpaper on screen.
            return photo.FilePath;
        }
    }

    /// <summary>Removes composed images whose source photo is no longer in rotation.</summary>
    public void Prune(IEnumerable<PhotoItem> keep)
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        var live = keep.Select(p => SafeName(p.Id)).ToHashSet(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(_directory, "*.jpg"))
        {
            // "<id>_<w>x<h>_<mode>.jpg" - the id is everything before the size suffix.
            string name = Path.GetFileNameWithoutExtension(file);
            int cut = name.LastIndexOf('_');
            cut = cut > 0 ? name.LastIndexOf('_', cut - 1) : -1;

            if (cut > 0 && live.Contains(name[..cut]))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Probably still set as the current wallpaper; it will go on a later pass.
            }
        }
    }

    private void Compose(
        string sourcePath,
        FitLayout layout,
        int monitorWidth,
        int monitorHeight,
        PhotoFitMode mode,
        string targetPath)
    {
        int stride = monitorWidth * 4;
        byte[] canvas = new byte[stride * monitorHeight];

        if (mode == PhotoFitMode.BlurredPadding)
        {
            DrawBlurredBackdrop(sourcePath, canvas, monitorWidth, monitorHeight);
        }
        else
        {
            // Opaque black. The buffer starts zeroed, so only alpha needs setting.
            for (int i = 3; i < canvas.Length; i += 4)
            {
                canvas[i] = 255;
            }
        }

        BitmapSource photo = LoadBgra(sourcePath, layout.Width, layout.Height);
        byte[] pixels = new byte[photo.PixelWidth * 4 * photo.PixelHeight];
        photo.CopyPixels(pixels, photo.PixelWidth * 4, 0);

        Blit(
            pixels, photo.PixelWidth, photo.PixelHeight,
            canvas, monitorWidth, monitorHeight,
            layout.OffsetX, layout.OffsetY);

        Save(canvas, monitorWidth, monitorHeight, targetPath);
    }

    private static void DrawBlurredBackdrop(string sourcePath, byte[] canvas, int width, int height)
    {
        // Shrink first, blur the thumbnail, then stretch it back over the whole screen.
        BitmapSource small = LoadBgra(sourcePath, BackdropSampleSize, BackdropSampleSize);
        int smallWidth = small.PixelWidth;
        int smallHeight = small.PixelHeight;

        byte[] source = new byte[smallWidth * 4 * smallHeight];
        small.CopyPixels(source, smallWidth * 4, 0);

        BoxBlur(source, smallWidth, smallHeight, BlurRadius, BlurPasses);

        // Cover, so the backdrop reaches every edge with no bars of its own.
        FitLayout cover = FitGeometry.Cover(smallWidth, smallHeight, width, height);
        double scaleX = (double)cover.Width / smallWidth;
        double scaleY = (double)cover.Height / smallHeight;

        for (int y = 0; y < height; y++)
        {
            double sourceY = (y - cover.OffsetY) / scaleY;
            int sy = Math.Clamp((int)sourceY, 0, smallHeight - 1);

            for (int x = 0; x < width; x++)
            {
                double sourceX = (x - cover.OffsetX) / scaleX;
                int sx = Math.Clamp((int)sourceX, 0, smallWidth - 1);

                int from = ((sy * smallWidth) + sx) * 4;
                int to = ((y * width) + x) * 4;

                canvas[to] = (byte)(source[from] * BackdropDimming);
                canvas[to + 1] = (byte)(source[from + 1] * BackdropDimming);
                canvas[to + 2] = (byte)(source[from + 2] * BackdropDimming);
                canvas[to + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Separable box blur, run several times over. Repeated box passes converge on a Gaussian, and
    /// each pass is a couple of adds per pixel rather than a kernel multiply.
    /// </summary>
    private static void BoxBlur(byte[] pixels, int width, int height, int radius, int passes)
    {
        if (radius < 1 || width < 2 || height < 2)
        {
            return;
        }

        byte[] scratch = new byte[pixels.Length];

        for (int pass = 0; pass < passes; pass++)
        {
            BlurAxis(pixels, scratch, width, height, radius, horizontal: true);
            BlurAxis(scratch, pixels, width, height, radius, horizontal: false);
        }
    }

    private static void BlurAxis(
        byte[] source, byte[] destination, int width, int height, int radius, bool horizontal)
    {
        int outer = horizontal ? height : width;
        int inner = horizontal ? width : height;

        for (int o = 0; o < outer; o++)
        {
            for (int i = 0; i < inner; i++)
            {
                int sumB = 0, sumG = 0, sumR = 0, count = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int sampled = i + k;
                    if (sampled < 0 || sampled >= inner)
                    {
                        continue;
                    }

                    int index = horizontal
                        ? ((o * width) + sampled) * 4
                        : ((sampled * width) + o) * 4;

                    sumB += source[index];
                    sumG += source[index + 1];
                    sumR += source[index + 2];
                    count++;
                }

                int target = horizontal
                    ? ((o * width) + i) * 4
                    : ((i * width) + o) * 4;

                destination[target] = (byte)(sumB / count);
                destination[target + 1] = (byte)(sumG / count);
                destination[target + 2] = (byte)(sumR / count);
                destination[target + 3] = 255;
            }
        }
    }

    /// <summary>Copies the photo onto the canvas at the given offset, clipping at the edges.</summary>
    private static void Blit(
        byte[] source, int sourceWidth, int sourceHeight,
        byte[] destination, int destinationWidth, int destinationHeight,
        int offsetX, int offsetY)
    {
        int copyWidth = Math.Min(sourceWidth, destinationWidth - offsetX);
        int copyHeight = Math.Min(sourceHeight, destinationHeight - offsetY);

        for (int y = 0; y < copyHeight; y++)
        {
            int from = y * sourceWidth * 4;
            int to = (((y + offsetY) * destinationWidth) + offsetX) * 4;
            Buffer.BlockCopy(source, from, destination, to, copyWidth * 4);
        }
    }

    private static void Save(byte[] pixels, int width, int height, string targetPath)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        // Encode beside the target then swap, so an interrupted write cannot leave a partial file
        // that later looks like a valid cache entry.
        string temp = targetPath + ".part";
        using (FileStream stream = File.Create(temp))
        {
            encoder.Save(stream);
        }

        File.Move(temp, targetPath, overwrite: true);
    }

    /// <summary>Reads the pixel dimensions without decoding the image.</summary>
    private static (int Width, int Height) ReadDimensions(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame frame = BitmapDecoder.Create(
            stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];

        return (frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>
    /// Decodes to the requested size in Bgra32. Scaling happens in the decoder, so a 4000px photo is
    /// never held in memory at full size just to be shrunk.
    /// </summary>
    private static BitmapSource LoadBgra(string path, int width, int height)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = Math.Max(1, width);
        image.DecodePixelHeight = Math.Max(1, height);
        image.UriSource = new Uri(path);
        image.EndInit();

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>
    /// Short, collision-resistant, filesystem-safe name for a photo id.
    ///
    /// Hashed rather than truncated because the local folder source uses the full file path as its
    /// id: two photos in different folders can share their last 64 characters, and truncation would
    /// have them overwrite each other's composed image.
    /// </summary>
    private static string SafeName(string id)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }
}
