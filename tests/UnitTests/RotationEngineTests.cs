using GooglePhotoWallpaper.Services;

namespace GooglePhotoWallpaper.Tests;

/// <summary>
/// Checks the rotation rule the app is built around: each monitor is one step further along a
/// shared list, and the whole window advances by one on every tick.
/// </summary>
internal static class RotationEngineTests
{
    private const string Alphabet = "ABCDEFG";

    public static void Run()
    {
        StaggeredAcrossTwoMonitors();
        StaggeredAcrossThreeMonitors();
        WrapsAroundTheEndOfTheList();
        SingleMonitorIsAPlainSequence();
        MoreMonitorsThanPhotosRepeatsRatherThanCrashing();
        EmptyLibraryIsHarmless();
        MirrorModeMatchesTheFirstMonitor();
        ShuffleKeepsMonitorsStaggered();
        ResumesFromASavedOffset();
        AdvanceIsIdempotentOverAFullCycle();
    }

    /// <summary>The exact sequence from the feature request: A/B, then B/C, then C/D.</summary>
    private static void StaggeredAcrossTwoMonitors()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false);

        Assert.Equal("tick 0 shows A and B", "AB", Render(engine, monitors: 2));
        engine.Advance();
        Assert.Equal("tick 1 shows B and C", "BC", Render(engine, monitors: 2));
        engine.Advance();
        Assert.Equal("tick 2 shows C and D", "CD", Render(engine, monitors: 2));
        engine.Advance();
        Assert.Equal("tick 3 shows D and E", "DE", Render(engine, monitors: 2));
    }

    private static void StaggeredAcrossThreeMonitors()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false);

        Assert.Equal("three monitors start at A/B/C", "ABC", Render(engine, monitors: 3));
        engine.Advance();
        Assert.Equal("three monitors move to B/C/D", "BCD", Render(engine, monitors: 3));
    }

    private static void WrapsAroundTheEndOfTheList()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false, startOffset: 6);

        Assert.Equal("last photo pairs with the first", "GA", Render(engine, monitors: 2));
        engine.Advance();
        Assert.Equal("offset wraps to zero", "AB", Render(engine, monitors: 2));
    }

    private static void SingleMonitorIsAPlainSequence()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false);

        Assert.Equal("single monitor starts at A", "A", Render(engine, monitors: 1));
        engine.Advance();
        Assert.Equal("single monitor moves to B", "B", Render(engine, monitors: 1));
    }

    private static void MoreMonitorsThanPhotosRepeatsRatherThanCrashing()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 2, shuffle: false);

        Assert.Equal("three monitors over two photos repeat", "ABA", Render(engine, monitors: 3));
    }

    private static void EmptyLibraryIsHarmless()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 0, shuffle: false);

        Assert.Equal("no photos yields no assignment", 0, engine.CurrentAssignment(2).Count);
        Assert.Equal("mirrored index reports nothing", -1, engine.CurrentMirroredIndex());

        engine.Advance();
        Assert.Equal("advancing an empty list is a no-op", 0, engine.Offset);
    }

    private static void MirrorModeMatchesTheFirstMonitor()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false);
        engine.Advance(3);

        Assert.Equal("mirror uses the same photo monitor 1 would get",
            engine.CurrentAssignment(2)[0],
            engine.CurrentMirroredIndex());
    }

    /// <summary>
    /// Shuffling reorders the list but must not break the relationship between monitors: what
    /// monitor 2 shows now is what monitor 1 shows next.
    /// </summary>
    private static void ShuffleKeepsMonitorsStaggered()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: true, shuffleSeed: 12345);

        bool staggered = true;
        for (int tick = 0; tick < 7; tick++)
        {
            IReadOnlyList<int> now = engine.CurrentAssignment(2);
            engine.Advance();
            IReadOnlyList<int> next = engine.CurrentAssignment(2);

            if (now[1] != next[0])
            {
                staggered = false;
                break;
            }
        }

        Assert.Equal("shuffled order still hands monitor 2's photo to monitor 1 next", true, staggered);

        var ordered = new RotationEngine();
        ordered.Load(photoCount: 7, shuffle: false, shuffleSeed: 12345);
        var shuffled = new RotationEngine();
        shuffled.Load(photoCount: 7, shuffle: true, shuffleSeed: 12345);

        Assert.Equal("shuffle actually changes the order",
            true,
            Render(ordered, 7) != Render(shuffled, 7));
    }

    private static void ResumesFromASavedOffset()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false, startOffset: 4);

        Assert.Equal("restored offset is honoured", 4, engine.Offset);
        Assert.Equal("restored offset renders from E", "EF", Render(engine, monitors: 2));
    }

    private static void AdvanceIsIdempotentOverAFullCycle()
    {
        var engine = new RotationEngine();
        engine.Load(photoCount: 7, shuffle: false);
        string before = Render(engine, monitors: 2);

        for (int i = 0; i < 7; i++)
        {
            engine.Advance();
        }

        Assert.Equal("a full cycle returns to the start", before, Render(engine, monitors: 2));
    }

    private static string Render(RotationEngine engine, int monitors)
        => string.Concat(engine.CurrentAssignment(monitors).Select(i => Alphabet[i % Alphabet.Length]));

}
