using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SynthModManager.Models;

namespace SynthModManager.Services;

public class GitHubService
{
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    readonly HttpClient _http;

    public GitHubService()
    {
        _http = new HttpClient();
        // GitHub API rejects requests without a User-Agent
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SynthModManager/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>Loads mods.json from a URL, falling back to the local copy next to the exe.</summary>
    public async Task<ModManifest> LoadManifestAsync(string? manifestUrl)
    {
        if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            try
            {
                var json = await _http.GetStringAsync(manifestUrl);
                var remote = JsonSerializer.Deserialize<ModManifest>(json, JsonOpts);
                if (remote is { Mods.Count: > 0 }) return remote;
            }
            catch { /* fall through to local */ }
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, "mods.json");
        if (File.Exists(localPath))
        {
            var local = JsonSerializer.Deserialize<ModManifest>(await File.ReadAllTextAsync(localPath), JsonOpts);
            if (local != null) return local;
        }

        return new ModManifest();
    }

    /// <summary>Latest release for "owner/repo". Unauthenticated = 60 req/hr, plenty for a mod list.</summary>
    public async Task<GitHubRelease?> GetLatestReleaseAsync(string repo)
    {
        var url = $"https://api.github.com/repos/{repo.Trim('/')}/releases/latest";
        using var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<GitHubRelease>(await resp.Content.ReadAsStringAsync(), JsonOpts);
    }

    /// <summary>Picks the asset to install: manifest regex first, then first .dll, then first .zip.</summary>
    public static GitHubAsset? PickAsset(GitHubRelease release, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
            var match = release.Assets.FirstOrDefault(a => rx.IsMatch(a.Name));
            if (match != null) return match;
        }
        return release.Assets.FirstOrDefault(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public async Task DownloadAsync(string url, string destinationFile, IProgress<double>? progress = null)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destinationFile);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total > 0) progress?.Report((double)readTotal / total);
        }
    }
}
