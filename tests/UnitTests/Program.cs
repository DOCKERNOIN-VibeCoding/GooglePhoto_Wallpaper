using GooglePhotoWallpaper.Tests;

Console.WriteLine("rotation engine");
RotationEngineTests.Run();

Console.WriteLine();
Console.WriteLine("shared album parser");
SharedAlbumParserTests.Run();

Console.WriteLine();
Console.WriteLine(Assert.Failures == 0
    ? "all tests passed"
    : $"{Assert.Failures} test(s) FAILED");

return Assert.Failures == 0 ? 0 : 1;
