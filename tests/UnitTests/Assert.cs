namespace GooglePhotoWallpaper.Tests;

/// <summary>Minimal assertion helper, so the tests need no external test framework.</summary>
internal static class Assert
{
    public static int Failures { get; private set; }

    public static void Equal<T>(string description, T expected, T actual)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        Report(ok, description, $"expected {expected}, got {actual}");
    }

    public static void True(string description, bool condition, string? detail = null)
        => Report(condition, description, detail ?? "expected true");

    private static void Report(bool ok, string description, string detail)
    {
        if (!ok)
        {
            Failures++;
        }

        Console.WriteLine(ok
            ? $"  PASS  {description}"
            : $"  FAIL  {description}  ({detail})");
    }
}
