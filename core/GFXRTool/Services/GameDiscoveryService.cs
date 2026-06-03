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
        var candidates = Directory
            .EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
            .Where(e => !IsUtilityExe(e) && !IsInExcludedDir(e))
            .ToList();

        if (!candidates.Any()) return null;

        // Score each candidate — higher wins.
        return candidates
            .Select(e => (ExePath: e, Score: ScoreExe(e, gameName)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => new FileInfo(x.ExePath).Length)
            .First().ExePath;
    }

    private static int ScoreExe(string exePath, string gameName)
    {
        var name     = System.IO.Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        var dirLower = exePath.ToLowerInvariant();

        // ── Unreal Engine: *Shipping* in Binaries/Win64 or Binaries/Win32 ────
        // These are always the real game process — prioritise absolutely.
        bool inBinaries = dirLower.Contains(@"\binaries\win64\") ||
                          dirLower.Contains(@"\binaries\win32\");
        bool isShipping = name.Contains("shipping");

        if (inBinaries && isShipping)  return 100;
        if (inBinaries && !isShipping) return  70;  // other Unreal exes (editor, etc.)
        if (isShipping)                return  80;  // shipping outside canonical path

        // ── Name matches game slug ────────────────────────────────────────────
        var slug = Slugify(gameName);
        if (slug.Length > 2 && Slugify(name).Contains(slug)) return 60;

        // ── Exe in a plausible game bin folder ────────────────────────────────
        bool inBinDir = dirLower.Contains(@"\bin\")  ||
                        dirLower.Contains(@"\bin64\") ||
                        dirLower.Contains(@"\x64\")   ||
                        dirLower.Contains(@"\win64\");
        if (inBinDir) return 40;

        // ── Root of install directory ─────────────────────────────────────────
        var installRoot = Path.GetDirectoryName(path)?.ToLowerInvariant() ?? "";
        // If the exe's parent IS the install root it gets a small bump
        return 10;
    }

    // Directories that only contain redistributables, tools, or setup programs —
    // never the real game executable.
    private static bool IsInExcludedDir(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains(@"\redistributables\")  ||
               lower.Contains(@"\redistributable\")   ||
               lower.Contains(@"\_commonredist\")      ||
               lower.Contains(@"\redist\")             ||
               lower.Contains(@"\setup\")              ||
               lower.Contains(@"\directx\")            ||
               lower.Contains(@"\dotnet\")             ||
               lower.Contains(@"\physx\")              ||
               lower.Contains(@"\support\")            ||
               lower.Contains(@"\installer\")          ||
               lower.Contains(@"\prerequisites\");
    }

    private static bool IsUtilityExe(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name is
            // Uninstallers / setup
            "unins000" or "uninstall" or "uninst" or "setup" or "install" or
            "dxsetup" or "dxwebsetup" or
            // Redistributables
            "vc_redist.x64" or "vc_redist.x86" or "vcredist_x64" or "vcredist_x86" or
            "vcredist" or "redist" or "dotnetfx" or "dotnetfx35client" or
            // Crash / error reporting
            "crashhandler" or "crashreporter" or "crashpad_handler" or
            "sentry" or "bugsplat" or
            // Launchers / bootstrappers that are NOT the game itself
            "launcher" or "bootstrapper" or
            // Valve / Steam tools
            "vconsole" or "vconsole2" or "steamwebhelper" or "gameoverlayui" or
            // Rockstar
            "socialclubsetup" or "social-club-setup" or "rockstarinstaller" or
            "rockstarlauncher" or "rgsc" or
            // Epic
            "epicgameslauncher" or "epicinstaller" or "epicgamesinstaller" or
            // Anti-cheat (standalone service exes, not the game)
            "easyanticheat" or "easyanticheat_setup" or "bservice" or
            "battleye" or "battleeye" or
            // Misc tools
            "x64launcher" or "x86launcher" or "python" or "pythonw" or
            "node" or "nvdisplay.container";
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
