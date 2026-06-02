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
                                string? captureOutputDir = null, bool deferCapture = false)
    {
        if (dlls.Count == 0) throw new InvalidOperationException("No DLLs to sideload.");

        var gameDir = Path.GetDirectoryName(game.ExecutablePath)!;
        var copied  = await StageDllsAsync(gameDir, dlls);

        WriteSettingsFile(gameDir, captureOutputDir, deferCapture);

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
                               IProgress<string>? progress = null,
                               CancellationToken ct = default)
    {
        if (dlls.Count == 0) throw new InvalidOperationException("No DLLs to sideload.");
        if (string.IsNullOrEmpty(game.LauncherId))
            throw new InvalidOperationException(
                $"'{game.Name}' has no launcher ID — use Standard deployment instead.");

        var gameDir         = Path.GetDirectoryName(game.ExecutablePath)!;
        var launcherExeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);

        progress?.Report($"Staging {dlls.Count} layer(s) into {gameDir}...");
        var copied = await StageDllsAsync(gameDir, dlls);

        // Write settings file so GFXR picks up output dir / trigger key without
        // needing environment variables (the game inherits Steam's env, not ours).
        WriteSettingsFile(gameDir, captureOutputDir, deferCapture);

        try
        {
            var protocolUrl = BuildProtocolUrl(game);
            progress?.Report($"Firing launcher: {protocolUrl}");
            Process.Start(new ProcessStartInfo(protocolUrl) { UseShellExecute = true });

            progress?.Report("Waiting for game process...");
            var gameProcess = await WaitForGameProcessInDirAsync(
                gameDir, launcherExeName, timeoutMs: 120_000, ct, progress);

            progress?.Report($"Attached to {gameProcess.ProcessName} (PID {gameProcess.Id}).");
            return (gameProcess, copied);
        }
        catch
        {
            CleanupStagedDlls(copied);
            DeleteSettingsFile(gameDir);
            throw;
        }
    }

    // ── Settings file ─────────────────────────────────────────────────────────
    // GFXR layers read gfxrecon_settings.json from the working directory at load
    // time. This is the only reliable way to pass config when the game is spawned
    // by Steam/Epic (the game inherits their environment, not ours).

    private static void WriteSettingsFile(string gameDir, string? captureOutputDir, bool deferCapture)
    {
        var settings = new Dictionary<string, object>
        {
            // Always enable capture — GFXR won't write anything without this.
            ["capture_file"] = Path.Combine(
                string.IsNullOrWhiteSpace(captureOutputDir) ? gameDir : captureOutputDir,
                "gfxrecon_capture.gfxr")
        };

        if (!string.IsNullOrWhiteSpace(captureOutputDir))
            settings["capture_file_dir"] = captureOutputDir;

        if (deferCapture)
            settings["capture_trigger"] = "F12";

        var json = JsonSerializer.Serialize(
            new Dictionary<string, object> { ["lunarg_gfxreconstruct"] = settings },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(gameDir, SettingsFileName), json);
    }

    internal static void DeleteSettingsFile(string gameDir)
    {
        try { File.Delete(Path.Combine(gameDir, SettingsFileName)); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildProtocolUrl(Game game) =>
        game.Source switch
        {
            "Steam" => $"steam://rungameid/{game.LauncherId}",
            "Epic"  => $"com.epicgames.launcher://apps/{game.LauncherId}?action=launch&silent=true",
            _       => throw new InvalidOperationException(
                            $"No protocol URL known for source '{game.Source}'.")
        };

    private static async Task<Process> WaitForGameProcessInDirAsync(
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
                if (proc.HasExited) continue;
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    if (exePath == null) continue;

                    var exeDir  = Path.GetFullPath(Path.GetDirectoryName(exePath)!).TrimEnd('\\', '/');
                    var exeName = Path.GetFileNameWithoutExtension(exePath);

                    if (exeDir.StartsWith(normalizedGameDir, StringComparison.OrdinalIgnoreCase) &&
                        !exeName.Equals(launcherExeName, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
                catch { }
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
