using GFXRTool.Models;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GFXRTool.Services;

public class GameDiscoveryService
{
    public async Task<List<Game>> DiscoverAllGamesAsync()
    {
        var steamTask = Task.Run(() => DiscoverSteamGames().ToList());
        var epicTask = Task.Run(() => DiscoverEpicGames().ToList());

        await Task.WhenAll(steamTask, epicTask);

        return steamTask.Result
            .Concat(epicTask.Result)
            .OrderBy(g => g.Name)
            .ToList();
    }

    private IEnumerable<Game> DiscoverSteamGames()
    {
        var steamPath = GetSteamPath();
        if (steamPath == null) yield break;

        foreach (var libPath in GetSteamLibraryPaths(steamPath))
        {
            var steamappsPath = Path.Combine(libPath, "steamapps");
            if (!Directory.Exists(steamappsPath)) continue;

            foreach (var manifest in Directory.GetFiles(steamappsPath, "appmanifest_*.acf"))
            {
                var game = ParseAcfManifest(manifest, libPath);
                if (game != null) yield return game;
            }
        }
    }

    private string? GetSteamPath()
    {
        var subkeys = new[]
        {
            @"SOFTWARE\Valve\Steam",
            @"SOFTWARE\WOW6432Node\Valve\Steam"
        };

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var sub in subkeys)
        {
            using var key = hive.OpenSubKey(sub);
            if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                return path;
        }

        return null;
    }

    private List<string> GetSteamLibraryPaths(string steamPath)
    {
        var paths = new List<string> { steamPath };

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return paths;

        var content = File.ReadAllText(vdfPath);
        foreach (Match m in Regex.Matches(content, @"""path""\s+""([^""]+)"""))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                paths.Add(p);
        }

        return paths;
    }

    private Game? ParseAcfManifest(string acfPath, string libraryPath)
    {
        try
        {
            var content = File.ReadAllText(acfPath);

            var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
            var dirMatch  = Regex.Match(content, @"""installdir""\s+""([^""]+)""");
            if (!nameMatch.Success || !dirMatch.Success) return null;

            var installPath = Path.Combine(libraryPath, "steamapps", "common", dirMatch.Groups[1].Value);
            if (!Directory.Exists(installPath)) return null;

            var exe = FindGameExecutable(installPath, nameMatch.Groups[1].Value);
            if (exe == null) return null;

            // AppId is encoded in the manifest filename: appmanifest_480.acf → "480"
            var fileName = Path.GetFileNameWithoutExtension(acfPath); // "appmanifest_480"
            var appId    = fileName.StartsWith("appmanifest_") ? fileName["appmanifest_".Length..] : null;

            return new Game
            {
                Name             = nameMatch.Groups[1].Value,
                ExecutablePath   = exe,
                InstallDirectory = installPath,
                Source           = "Steam",
                LauncherId       = appId
            };
        }
        catch
        {
            return null;
        }
    }

    private string? FindGameExecutable(string installPath, string gameName)
    {
        // Check root first, then recurse up to 2 levels deep
        var candidates = Directory
            .EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
            .Where(e => !IsUtilityExe(e))
            .Take(30)
            .ToList();

        if (!candidates.Any()) return null;

        // Prefer exe whose filename resembles the game name
        var slug = Slugify(gameName);
        return candidates.FirstOrDefault(e => Slugify(Path.GetFileNameWithoutExtension(e)).Contains(slug))
               ?? candidates.OrderByDescending(e => new FileInfo(e).Length).First();
    }

    private static bool IsUtilityExe(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name is "unins000" or "uninstall" or "setup" or "dxsetup"
                    or "vc_redist.x64" or "vc_redist.x86" or "dotnetfx"
                    or "crashhandler" or "crashreporter" or "launcher"
                    or "redist" or "vcredist";
    }

    private static string Slugify(string s) =>
        Regex.Replace(s, @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();

    private IEnumerable<Game> DiscoverEpicGames()
    {
        var manifestsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsPath)) yield break;

        foreach (var itemFile in Directory.GetFiles(manifestsPath, "*.item"))
        {
            Game? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(itemFile));
                var root = doc.RootElement;

                if (!root.TryGetProperty("DisplayName", out var nameProp)) continue;
                if (!root.TryGetProperty("InstallLocation", out var installProp)) continue;
                if (!root.TryGetProperty("LaunchExecutable", out var exeProp)) continue;

                var installDir = installProp.GetString();
                var launchExe = exeProp.GetString();
                if (installDir == null || launchExe == null) continue;

                var exePath = Path.Combine(installDir, launchExe);
                if (!File.Exists(exePath)) continue;

                game = new Game
                {
                    Name             = nameProp.GetString() ?? Path.GetFileNameWithoutExtension(exePath),
                    ExecutablePath   = exePath,
                    InstallDirectory = installDir,
                    Source           = "Epic",
                    // "AppName" is the identifier used by the com.epicgames.launcher://apps/<AppName>?action=launch URL
                    LauncherId       = root.TryGetProperty("AppName", out var appNameProp) ? appNameProp.GetString() : null
                };
            }
            catch { }

            if (game != null) yield return game;
        }
    }
}
