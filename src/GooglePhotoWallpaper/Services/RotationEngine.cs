namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Decides which picture lands on which monitor.
///
/// Monitors read off one shared list at a fixed stride, so monitor <c>i</c> always shows the
/// picture that monitor <c>i-1</c> showed on the previous tick:
///
///     tick 0 -> monitor1=A monitor2=B
///     tick 1 -> monitor1=B monitor2=C
///     tick 2 -> monitor1=C monitor2=D
///
/// The offset advances by one per tick and wraps at the end of the list.
/// </summary>
public sealed class RotationEngine
{
    private readonly object _gate = new();
    private IReadOnlyList<int> _order = Array.Empty<int>();
    private int _photoCount;
    private int _offset;

    /// <summary>Number of photos currently in rotation.</summary>
    public int PhotoCount
    {
        get { lock (_gate) { return _photoCount; } }
    }

    public int Offset
    {
        get { lock (_gate) { return _photoCount == 0 ? 0 : _offset % _photoCount; } }
    }

    /// <summary>
    /// Points the engine at a new photo list. <paramref name="shuffle"/> permutes the reading order
    /// once; the staggered relationship between monitors is preserved either way.
    /// </summary>
    public void Load(int photoCount, bool shuffle, int startOffset = 0, int? shuffleSeed = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(photoCount);

        lock (_gate)
        {
            _photoCount = photoCount;

            var order = Enumerable.Range(0, photoCount).ToArray();
            if (shuffle && photoCount > 1)
            {
                var rng = shuffleSeed is null ? Random.Shared : new Random(shuffleSeed.Value);
                rng.Shuffle(order);
            }

            _order = order;
            _offset = photoCount == 0 ? 0 : ((startOffset % photoCount) + photoCount) % photoCount;
        }
    }

    /// <summary>
    /// Indices into the photo list for each monitor, at the current offset.
    /// Returns an empty list when there are no photos.
    /// </summary>
    public IReadOnlyList<int> CurrentAssignment(int monitorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(monitorCount);

        lock (_gate)
        {
            if (_photoCount == 0 || monitorCount == 0)
            {
                return Array.Empty<int>();
            }

            var result = new int[monitorCount];
            for (int i = 0; i < monitorCount; i++)
            {
                result[i] = _order[(_offset + i) % _photoCount];
            }

            return result;
        }
    }

    /// <summary>Every monitor shows the same picture - the one monitor 1 would get.</summary>
    public int CurrentMirroredIndex()
    {
        lock (_gate)
        {
            return _photoCount == 0 ? -1 : _order[_offset % _photoCount];
        }
    }

    /// <summary>Steps the window forward by one picture.</summary>
    public void Advance(int steps = 1)
    {
        lock (_gate)
        {
            if (_photoCount == 0)
            {
                return;
            }

            _offset = (((_offset + steps) % _photoCount) + _photoCount) % _photoCount;
        }
    }
}
