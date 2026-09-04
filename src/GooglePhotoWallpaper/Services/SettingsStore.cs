using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GooglePhotoWallpaper.Models;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Reads and writes settings.json. Only preferences go in here - tokens live in the DPAPI-encrypted
/// token store, never in this file.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    public SettingsStore(string? path = null) => _path = path ?? AppPaths.SettingsFile;

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options)
                    ?? new AppSettings();
            }
            catch (Exception)
            {
                // A hand-edited or truncated file should not stop the app from starting.
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Write beside the target then swap, so a crash mid-write cannot leave a half file.
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
        }
    }
}
