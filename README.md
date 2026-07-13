# Synth Riders Mod Manager

A WPF desktop app that lets users browse a curated list of Synth Riders mods, auto-fetches the latest release of each mod from GitHub, and installs it to the correct folder (`Mods/` under the game install, or full game-root layout for zips that ship `Mods/`, `UserLibs/`, etc.).

## Features

- **Auto-detects Synth Riders** via the Steam registry + `libraryfolders.vdf` (app ID 885000), with manual Browse fallback.
- **MelonLoader check** — warns before installing if `version.dll` / `MelonLoader/` aren't present.
- **GitHub Releases integration** — queries `releases/latest` for each mod, shows installed vs. latest version, one-click Install / Update / Uninstall.
- **Smart asset handling** — installs loose `.dll` assets into `Mods/`; extracts zips (with zip-slip protection), respecting a game-root layout if the zip contains `Mods/`, `UserLibs/`, `UserData/`, or `Plugins/` folders. An optional `assetPattern` regex per mod picks the right asset when releases ship multiple (e.g. PCVR vs Quest).
- **Clean uninstall** — every installed file is recorded in `%AppData%\SynthModManager\installed.json` so uninstall removes exactly what was added.
- **Remote manifest** — the mod list is a `mods.json` you host on GitHub; users get new mods without updating the app. Falls back to the local `mods.json` next to the exe if offline.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
cd SynthModManager
dotnet build
dotnet run
```

### Release build (single exe for distribution)

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Output lands in `bin\Release\net8.0-windows\win-x64\publish\`. `--self-contained false` keeps the exe small (~1 MB) but users need the .NET 8 Desktop Runtime; swap to `--self-contained true` for a ~70 MB exe with zero dependencies (better for community distribution).

## Setting up your manifest

1. Create a public GitHub repo, e.g. `synth-mod-manifest`, containing one file: `mods.json`.
2. Edit `DefaultManifestUrl` at the top of `MainWindow.xaml.cs` to point at the raw URL:
   `https://raw.githubusercontent.com/YOURNAME/synth-mod-manifest/main/mods.json`
3. Each manifest entry:

```json
{
  "id": "unique-lowercase-id",
  "name": "Display Name",
  "author": "Author",
  "description": "What it does.",
  "repo": "githubowner/repo-name",
  "assetPattern": "(?i)pcvr.*\\.zip$"   // optional regex; omit to auto-pick first .dll then first .zip
}
```

The repo just needs GitHub **Releases** with a `.dll` or `.zip` asset attached — which is how most MelonLoader mods are already published. Adding a mod to the community list is a one-line PR to your manifest repo.

> The bundled `mods.json` contains placeholder entries — replace `owner/repo-name` with real repos before shipping. Verify each mod's release layout once (dll vs zip, asset names) and add an `assetPattern` where needed.

## Rate limits

Unauthenticated GitHub API calls are limited to 60/hour per IP — one call per mod per refresh, so a 20-mod list allows ~3 refreshes per hour per user. Fine for normal use; if the community list grows large, you can later cache release data into the manifest itself via a scheduled GitHub Action.

## File map

| File | Purpose |
|---|---|
| `MainWindow.xaml(.cs)` | UI + orchestration (detect, list, install, uninstall, log) |
| `Services/SteamLocator.cs` | Steam registry + VDF parsing, MelonLoader detection |
| `Services/GitHubService.cs` | Manifest loading, `releases/latest`, asset picking, downloads |
| `Services/ModInstaller.cs` | DLL/zip install with zip-slip guard, install DB, settings |
| `Models/ModModels.cs` | Manifest/GitHub/DB models + `ModRow` view model |
| `mods.json` | Local fallback manifest (edit before shipping) |
