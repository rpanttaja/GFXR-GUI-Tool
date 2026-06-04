using GFXRTool.Models;
using System.Diagnostics;
using System.Text.Json;

namespace GFXRTool.Services;

public class GameLauncherService
{
    private const string SettingsFileName = "gfxrecon_settings.json";

    // Each entry: (deployed path, backup path or null if nothing was displaced)
    public async Task<(Process Process, IReadOnlyList<(string Dest, string? Backup)> CopiedPaths)>
        LaunchWithSideloadAsync(Game game, IReadOnlyList<GfxrDll> dlls,
                                string? captureOutputDir = null, bool deferCapture = false,
                                string triggerKey = "F12")
    {
        if (dlls.Count == 0) throw new InvalidOperationException("No DLLs to sideload.");

        var gameDir = Path.GetDirectoryName(game.ExecutablePath)!;
        var copied  = new List<(string Dest, string? Backup)>(await StageDllsAsync(gameDir, dlls));

        WriteSettingsFile(gameDir, captureOutputDir, deferCapture, triggerKey);
        // Include settings file in copied list so it's auto-cleaned on exit.
        copied.Add((Path.Combine(gameDir, SettingsFileName), null));

        var psi = new ProcessStartInfo
        {
            FileName         = game.ExecutablePath,
            WorkingDirectory = gameDir,
            UseShellExecute  = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start game process.");

        return (process, copied);
    }

    public async Task<(Process Process, IReadOnlyList<(string Dest, string? Backup)> CopiedPaths)>
        LaunchViaLauncherAsync(Game game, IReadOnlyList<GfxrDll> dlls,
                               string? captureOutputDir = null, bool deferCapture = false,
                               string triggerKey = "F12",
                               IProgress<string>? progress = null,
                               CancellationToken ct = default)
    {
        if (dlls.Count == 0) throw new InvalidOperationException("No DLLs to sideload.");
        if (string.IsNullOrEmpty(game.LauncherId))
            throw new InvalidOperationException(
                $"'{game.Name}' has no launcher ID — use Standard deployment instead.");

        var gameDir         = Path.GetFullPath(Path.GetDirectoryName(game.ExecutablePath)!);
        var launcherExeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);

        progress?.Report($"Staging {dlls.Count} layer(s) into {gameDir}...");
        var copied = new List<(string Dest, string? Backup)>(await StageDllsAsync(gameDir, dlls));
        WriteSettingsFile(gameDir, captureOutputDir, deferCapture, triggerKey);
        copied.Add((Path.Combine(gameDir, SettingsFileName), null));

        try
        {
            var protocolUrl = BuildProtocolUrl(game);
            progress?.Report($"Firing launcher: {protocolUrl}");
            Process.Start(new ProcessStartInfo(protocolUrl) { UseShellExecute = true });

            progress?.Report("Waiting for game process...");
            var gameProcess = await WaitForGameProcessInDirAsync(
                gameDir, launcherExeName, timeoutMs: 120_000, ct, progress);

            // Check if the actual game exe lives in a subdirectory (e.g. Squad.exe in
            // Squad\Binaries\Win64\). Windows DLL search order starts in the exe's own
            // directory, so we need the GFXR DLLs and settings file there too.
            try
            {
                var actualExeDir = Path.GetFullPath(
                    Path.GetDirectoryName(gameProcess.MainModule!.FileName)!);

                if (!actualExeDir.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report($"Game exe is in subdirectory — re-staging to: {actualExeDir}");
                    var extraCopied = await StageDllsAsync(actualExeDir, dlls);
                    copied.AddRange(extraCopied);
                    WriteSettingsFile(actualExeDir, captureOutputDir, deferCapture, triggerKey);
                    copied.Add((Path.Combine(actualExeDir, SettingsFileName), null));
                }
            }
            catch { /* protected process — gameDir staging is still in place */ }

            progress?.Report($"Attached to {gameProcess.ProcessName} (PID {gameProcess.Id}).");
            return (gameProcess, copied);
        }
        catch
        {
            CleanupStagedDlls(copied);
            throw;
        }
    }

    // ── Settings file ─────────────────────────────────────────────────────────
    // GFXR layers read gfxrecon_settings.json from the working directory at load
    // time. This is the only reliable way to pass config when the game is spawned
    // by Steam/Epic (the game inherits their environment, not ours).

    private static void WriteSettingsFile(string gameDir, string? captureOutputDir,
                                           bool deferCapture, string triggerKey = "F12")
    {
        // Resolve the output directory — default to the chosen dir, never the game dir.
        var outDir = string.IsNullOrWhiteSpace(captureOutputDir) ? gameDir : captureOutputDir;
        Directory.CreateDirectory(outDir);

        var settings = new Dictionary<string, object>
        {
            ["capture_file"] = System.IO.Path.Combine(outDir, "gfxrecon_capture.gfxr"),
        };

        if (deferCapture)
            settings["capture_trigger"] = triggerKey;

        var json = JsonSerializer.Serialize(
            new Dictionary<string, object> { ["lunarg_gfxreconstruct"] = settings },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(System.IO.Path.Combine(gameDir, SettingsFileName), json);
    }

    internal static void DeleteSettingsFile(string gameDir)
    {
        try { File.Delete(Path.Combine(gameDir, SettingsFileName)); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static string BuildProtocolUrl(Game game) =>
        game.Source switch
        {
            "Steam" => $"steam://rungameid/{game.LauncherId}",
            "Epic"  => $"com.epicgames.launcher://apps/{game.LauncherId}?action=launch&silent=true",
            _       => throw new InvalidOperationException(
                            $"No protocol URL known for source '{game.Source}'.")
        };

    internal static async Task<Process> WaitForGameProcessInDirAsync(
        string gameDir, string launcherExeName,
        int timeoutMs, CancellationToken ct,
        IProgress<string>? progress = null)
    {
        var deadline          = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var normalizedGameDir = Path.GetFullPath(gameDir).TrimEnd('\\', '/');
        const int pollMs      = 500;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    // HasExited can throw Access Denied on protected processes — must be inside try
                    if (proc.HasExited) continue;

                    var exePath = proc.MainModule?.FileName;
                    if (exePath == null) continue;

                    var exeDir  = Path.GetFullPath(Path.GetDirectoryName(exePath)!).TrimEnd('\\', '/');
                    var exeName = Path.GetFileNameWithoutExtension(exePath);

                    if (exeDir.StartsWith(normalizedGameDir, StringComparison.OrdinalIgnoreCase) &&
                        !exeName.Equals(launcherExeName, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
                catch { /* access denied on protected/system processes — skip */ }
            }

            await Task.Delay(pollMs, ct);
        }

        throw new TimeoutException(
            $"No game process found under '{gameDir}' within {timeoutMs / 1000}s. " +
            "The launcher may need longer, or EAC may have blocked the launch.");
    }

    internal static void CleanupStagedDlls(IReadOnlyList<(string Dest, string? Backup)> copied)
    {
        foreach (var (dest, backup) in copied)
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            if (backup != null)
                try { if (File.Exists(backup)) File.Move(backup, dest, overwrite: true); } catch { }
        }
    }

    private static Task<IReadOnlyList<(string Dest, string? Backup)>> StageDllsAsync(
        string targetDir, IReadOnlyList<GfxrDll> dlls)
    {
        var copied = new List<(string Dest, string? Backup)>();

        foreach (var dll in dlls)
        {
            var dest   = Path.Combine(targetDir, Path.GetFileName(dll.Path));
            var backup = dest + ".gfxr_bak";

            string? savedBackup = null;
            if (File.Exists(dest))
            {
                File.Move(dest, backup, overwrite: true);
                savedBackup = backup;
            }

            try
            {
                File.Copy(dll.Path, dest, overwrite: true);
            }
            catch
            {
                if (savedBackup != null && !File.Exists(dest))
                    try { File.Move(savedBackup, dest); } catch { }
                throw;
            }

            copied.Add((dest, savedBackup));
        }

        return Task.FromResult<IReadOnlyList<(string, string?)>>(copied);
    }
}
