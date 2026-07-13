using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SynthModManager.Models;
using SynthModManager.Services;

namespace SynthModManager;

public partial class MainWindow : Window
{
    // Set this to your hosted manifest (raw GitHub URL). Local mods.json is the fallback.
    const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/YOUR_GITHUB_USERNAME/synth-mod-manifest/main/mods.json";

    readonly GitHubService _github = new();
    readonly ModInstaller _installer = new();
    readonly ObservableCollection<ModRow> _mods = new();

    AppSettings _settings = new();
    InstalledDb _db = new();
    string? _gamePath;

    public MainWindow()
    {
        InitializeComponent();
        ModList.ItemsSource = _mods;
        Loaded += async (_, _) => await InitializeAsync();
    }

    async Task InitializeAsync()
    {
        _settings = _installer.LoadSettings();
        _db = _installer.LoadDb();

        // 1. Locate game
        _gamePath = SteamLocator.IsValidGamePath(_settings.GamePath)
            ? _settings.GamePath
            : SteamLocator.FindSynthRiders();

        if (_gamePath != null)
        {
            GamePathBox.Text = _gamePath;
            Log($"Found Synth Riders at: {_gamePath}");
        }
        else
        {
            GamePathBox.Text = "Not found — click Browse and select your Synth Riders folder";
            Log("Could not auto-detect Synth Riders. Use Browse to locate SynthRiders.exe's folder.");
        }
        UpdateMelonStatus();

        // 2. Load manifest + latest releases
        await LoadModsAsync();
    }

    async Task LoadModsAsync()
    {
        _mods.Clear();
        Log("Loading mod manifest…");

        var manifest = await _github.LoadManifestAsync(_settings.ManifestUrl ?? DefaultManifestUrl);
        if (manifest.Mods.Count == 0)
        {
            Log("Manifest is empty or unreachable. Check your internet connection or mods.json.");
            return;
        }

        foreach (var m in manifest.Mods)
        {
            var row = new ModRow(m);
            if (_db.Mods.TryGetValue(m.Id, out var rec)) row.InstalledVersion = rec.Version;
            _mods.Add(row);
        }

        // Fetch latest releases in parallel
        await Task.WhenAll(_mods.Select(async row =>
        {
            try
            {
                var release = await _github.GetLatestReleaseAsync(row.Manifest.Repo);
                await Dispatcher.InvokeAsync(() =>
                {
                    row.LatestRelease = release;
                    row.LatestVersion = release?.TagName ?? "unavailable";
                });
            }
            catch
            {
                await Dispatcher.InvokeAsync(() => row.LatestVersion = "error");
            }
        }));

        Log($"Loaded {_mods.Count} mods.");
    }

    async void InstallOrUpdate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ModRow row) return;

        if (_gamePath == null)
        {
            MessageBox.Show(this, "Set your Synth Riders folder first (Browse…).", "No game folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SteamLocator.HasMelonLoader(_gamePath))
        {
            var result = MessageBox.Show(this,
                "MelonLoader doesn't appear to be installed. Mods won't load without it.\n\nInstall the mod anyway?",
                "MelonLoader missing", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        if (row.LatestRelease == null)
        {
            Log($"{row.Name}: no release information available.");
            return;
        }

        var asset = GitHubService.PickAsset(row.LatestRelease, row.Manifest.AssetPattern);
        if (asset == null)
        {
            Log($"{row.Name}: latest release has no .dll or .zip asset.");
            return;
        }

        row.Busy = true;
        try
        {
            Log($"{row.Name}: downloading {asset.Name} ({asset.Size / 1024} KB)…");
            var tmp = Path.Combine(Path.GetTempPath(), asset.Name);
            await _github.DownloadAsync(asset.BrowserDownloadUrl, tmp);

            // If updating, remove the old files first so renames don't leave orphans
            if (_db.Mods.TryGetValue(row.Manifest.Id, out var oldRec))
                _installer.Uninstall(_gamePath, oldRec);

            var files = _installer.InstallAsset(_gamePath, tmp, asset.Name);
            File.Delete(tmp);

            _db.Mods[row.Manifest.Id] = new InstalledMod
            {
                Version = row.LatestRelease.TagName,
                Files = files,
                InstalledUtc = DateTime.UtcNow
            };
            _installer.SaveDb(_db);

            row.InstalledVersion = row.LatestRelease.TagName;
            Log($"{row.Name}: installed {row.LatestRelease.TagName} ({files.Count} file(s)).");
        }
        catch (Exception ex)
        {
            Log($"{row.Name}: install failed — {ex.Message}");
            MessageBox.Show(this, ex.Message, $"Failed to install {row.Name}",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            row.Busy = false;
        }
    }

    void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ModRow row) return;
        if (_gamePath == null) return;
        if (!_db.Mods.TryGetValue(row.Manifest.Id, out var rec)) return;

        _installer.Uninstall(_gamePath, rec);
        _db.Mods.Remove(row.Manifest.Id);
        _installer.SaveDb(_db);
        row.InstalledVersion = null;
        Log($"{row.Name}: uninstalled.");
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select SynthRiders.exe",
            Filter = "Synth Riders|SynthRiders.exe",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        var folder = Path.GetDirectoryName(dlg.FileName)!;
        if (!SteamLocator.IsValidGamePath(folder))
        {
            MessageBox.Show(this, "That doesn't look like a Synth Riders folder.", "Invalid folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _gamePath = folder;
        GamePathBox.Text = folder;
        _settings.GamePath = folder;
        _installer.SaveSettings(_settings);
        UpdateMelonStatus();
        Log($"Game folder set to: {folder}");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadModsAsync();

    void UpdateMelonStatus()
    {
        if (_gamePath == null)
        {
            MelonStatus.Text = "MelonLoader: unknown";
            return;
        }
        MelonStatus.Text = SteamLocator.HasMelonLoader(_gamePath)
            ? "MelonLoader: ✔ installed"
            : "MelonLoader: ✖ not found";
    }

    void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        LogBox.ScrollToEnd();
    }
}
