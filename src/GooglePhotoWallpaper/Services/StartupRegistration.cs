using System.Diagnostics;
using Microsoft.Win32;

namespace GooglePhotoWallpaper.Services;

/// <summary>
/// Registers the app under HKCU Run so it comes back after a reboot.
///
/// Per-user rather than machine-wide on purpose: the settings, cache and tokens are all per-user,
/// and HKCU needs no elevation - which keeps the app installer-free.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GooglePhotoWallpaper";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Contains(ExecutablePath(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("시작 프로그램 레지스트리 키를 열 수 없습니다.");

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath()}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// The real .exe. Process.MainModule is used rather than the assembly location because a
    /// single-file publish leaves the managed assembly with no path on disk.
    /// </summary>
    private static string ExecutablePath()
    {
        using Process process = Process.GetCurrentProcess();
        return process.MainModule?.FileName ?? Environment.ProcessPath ?? string.Empty;
    }
}
