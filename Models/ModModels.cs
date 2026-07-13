using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SynthModManager.Models;

// ---------- Manifest (mods.json, remote or local) ----------

public class ModManifest
{
    [JsonPropertyName("manifestVersion")] public int ManifestVersion { get; set; } = 1;
    [JsonPropertyName("mods")] public List<ManifestMod> Mods { get; set; } = new();
}

public class ManifestMod
{
    /// <summary>Unique id, e.g. "srperformancemeter"</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>GitHub "owner/repo"</summary>
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    /// <summary>Optional regex to pick the right release asset (e.g. "(?i)pcvr.*\\.zip$"). Defaults to first .dll, then first .zip.</summary>
    [JsonPropertyName("assetPattern")] public string? AssetPattern { get; set; }
}

// ---------- GitHub API (subset) ----------

public class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
}

public class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

// ---------- Local install records (installed.json in %AppData%) ----------

public class InstalledDb
{
    [JsonPropertyName("mods")] public Dictionary<string, InstalledMod> Mods { get; set; } = new();
}

public class InstalledMod
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    /// <summary>Paths relative to the game root, so uninstall knows exactly what to remove.</summary>
    [JsonPropertyName("files")] public List<string> Files { get; set; } = new();
    [JsonPropertyName("installedUtc")] public DateTime InstalledUtc { get; set; }
}

// ---------- App settings ----------

public class AppSettings
{
    [JsonPropertyName("gamePath")] public string? GamePath { get; set; }
    [JsonPropertyName("manifestUrl")] public string? ManifestUrl { get; set; }
}

// ---------- View model for the mod list ----------

public class ModRow : INotifyPropertyChanged
{
    public ManifestMod Manifest { get; }
    public ModRow(ManifestMod manifest) => Manifest = manifest;

    public string Name => Manifest.Name;
    public string Author => Manifest.Author;
    public string Description => Manifest.Description;
    public string Repo => Manifest.Repo;

    string _latestVersion = "…";
    public string LatestVersion { get => _latestVersion; set { _latestVersion = value; Notify(); Notify(nameof(StatusText)); Notify(nameof(ActionLabel)); } }

    string? _installedVersion;
    public string? InstalledVersion { get => _installedVersion; set { _installedVersion = value; Notify(); Notify(nameof(StatusText)); Notify(nameof(ActionLabel)); Notify(nameof(CanUninstall)); } }

    bool _busy;
    public bool Busy { get => _busy; set { _busy = value; Notify(); Notify(nameof(NotBusy)); } }
    public bool NotBusy => !_busy;

    public GitHubRelease? LatestRelease { get; set; }

    public string StatusText =>
        InstalledVersion == null ? "Not installed"
        : InstalledVersion == LatestVersion ? $"Installed {InstalledVersion}"
        : $"Installed {InstalledVersion} → update {LatestVersion}";

    public string ActionLabel =>
        InstalledVersion == null ? "Install"
        : InstalledVersion == LatestVersion ? "Reinstall"
        : "Update";

    public bool CanUninstall => InstalledVersion != null;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
