using System.IO;
using Microsoft.Win32;

namespace SynthModManager.Services;

/// <summary>Finds the Synth Riders install folder by walking Steam's library folders.</summary>
public static class SteamLocator
{
    public const string SynthRidersAppId = "885000";
    public const string GameExeName = "SynthRiders.exe";

    public static string? FindSynthRiders()
    {
        var steamPath = GetSteamPath();
        if (steamPath == null) return null;

        foreach (var library in GetLibraryFolders(steamPath))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SynthRidersAppId}.acf");
            if (!File.Exists(manifest)) continue;

            var installDir = ReadAcfValue(manifest, "installdir");
            if (installDir == null) continue;

            var gamePath = Path.Combine(library, "steamapps", "common", installDir);
            if (IsValidGamePath(gamePath)) return gamePath;
        }

        // Last-ditch: default location
        var fallback = Path.Combine(steamPath, "steamapps", "common", "SynthRiders");
        return IsValidGamePath(fallback) ? fallback : null;
    }

    public static bool IsValidGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, GameExeName));

    public static bool HasMelonLoader(string gamePath) =>
        File.Exists(Path.Combine(gamePath, "version.dll")) &&
        Directory.Exists(Path.Combine(gamePath, "MelonLoader"));

    static string? GetSteamPath()
    {
        // 64-bit view first, then WOW6432Node, then HKCU
        foreach (var (hive, key) in new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam"),
            (Registry.CurrentUser,  @"SOFTWARE\Valve\Steam"),
        })
        {
            try
            {
                using var k = hive.OpenSubKey(key);
                var val = k?.GetValue("InstallPath") as string ?? k?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                    return val.Replace('/', '\\');
            }
            catch { /* ignore and try next */ }
        }
        return null;
    }

    static IEnumerable<string> GetLibraryFolders(string steamPath)
    {
        yield return steamPath;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        // Cheap VDF parse: grab every "path" "X:\\..." line
        foreach (var line in File.ReadLines(vdf))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries)
                               .Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (parts.Length >= 2)
            {
                var path = parts[^1].Replace(@"\\", @"\");
                if (Directory.Exists(path)) yield return path;
            }
        }
    }

    static string? ReadAcfValue(string acfPath, string key)
    {
        foreach (var line in File.ReadLines(acfPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith($"\"{key}\"", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries)
                               .Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (parts.Length >= 2) return parts[^1];
        }
        return null;
    }
}
