using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SynthModManager.Models;

namespace SynthModManager.Services;

public class ModInstaller
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string AppDataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SynthModManager");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static string DbPath => Path.Combine(AppDataDir, "installed.json");
    static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public InstalledDb LoadDb()
    {
        try
        {
            if (File.Exists(DbPath))
                return JsonSerializer.Deserialize<InstalledDb>(File.ReadAllText(DbPath)) ?? new InstalledDb();
        }
        catch { }
        return new InstalledDb();
    }

    public void SaveDb(InstalledDb db) => File.WriteAllText(DbPath, JsonSerializer.Serialize(db, JsonOpts));

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings s) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOpts));

    /// <summary>
    /// Installs a downloaded asset (.dll or .zip) into the game folder.
    /// Returns the list of files written, relative to the game root.
    /// </summary>
    public List<string> InstallAsset(string gamePath, string downloadedFile, string assetName)
    {
        var written = new List<string>();
        var modsDir = Path.Combine(gamePath, "Mods");
        Directory.CreateDirectory(modsDir);

        if (assetName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var rel = Path.Combine("Mods", assetName);
            File.Copy(downloadedFile, Path.Combine(gamePath, rel), overwrite: true);
            written.Add(rel);
            return written;
        }

        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(downloadedFile);

            // Does the zip already contain a game-root layout (Mods/, UserLibs/, UserData/, Plugins/)?
            bool hasRootLayout = zip.Entries.Any(e =>
                e.FullName.Replace('\\', '/').StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Replace('\\', '/').StartsWith("UserLibs/", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Replace('\\', '/').StartsWith("UserData/", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Replace('\\', '/').StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase));

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

                string relPath;
                var normalized = entry.FullName.Replace('\\', '/');

                if (hasRootLayout)
                {
                    // Extract with the zip's own structure, relative to game root
                    relPath = normalized.Replace('/', Path.DirectorySeparatorChar);
                }
                else if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // Loose DLLs go straight into Mods/
                    relPath = Path.Combine("Mods", entry.Name);
                }
                else
                {
                    continue; // skip readmes etc. in loose zips
                }

                var fullPath = Path.GetFullPath(Path.Combine(gamePath, relPath));

                // Zip-slip guard: never write outside the game folder
                if (!fullPath.StartsWith(Path.GetFullPath(gamePath), StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                entry.ExtractToFile(fullPath, overwrite: true);
                written.Add(relPath);
            }
            return written;
        }

        throw new InvalidOperationException($"Unsupported asset type: {assetName}");
    }

    public void Uninstall(string gamePath, InstalledMod record)
    {
        foreach (var rel in record.Files)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(gamePath, rel));
                if (full.StartsWith(Path.GetFullPath(gamePath), StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                    File.Delete(full);
            }
            catch { /* best effort */ }
        }
    }
}
