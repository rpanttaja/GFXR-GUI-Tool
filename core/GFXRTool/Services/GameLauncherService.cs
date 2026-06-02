using GFXRTool.Models;
using System.Diagnostics;

namespace GFXRTool.Services;

public class GameLauncherService
{
    // Each entry: (deployed path, backup path or null if nothing was displaced)
    public async Task<(Process Process, IReadOnlyList<(string Dest, string? Backup)> CopiedPaths)>
        LaunchWithSideloadAsync(Game game, IReadOnlyList<GfxrDll> dlls)
    {
        if (dlls.Count == 0) throw new InvalidOperationException("No DLLs to sideload.");

        var gameDir = Path.GetDirectoryName(game.ExecutablePath)!;
        var copied  = await StageDllsAsync(gameDir, dlls);

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

    // Stages DLLs into the game directory, fires the native launcher URL (steam:// or Epic),
    // then polls until the actual game executable appears as a process.
    // EAC/BattlEye require the process to originate from the official launcher, so we must
    // NOT CreateProcess the game directly — the launcher does that for us.
    public async Task<(Process Process, IReadOnlyList<(string Dest, string? Backup)> CopiedPaths)>
        LaunchViaLauncherAsync(Game game, IReadOnlyList<GfxrDll> dlls,
                               IProgress<string>? progress = null,
                               CancellationToken ct = default)
    {
        if (dlls.Count == 0) throw new InvalidOperationException("No DLLs to sideload.");
        if (string.IsNullOrEmpty(game.LauncherId))
            throw new InvalidOperationException(
                $"'{game.Name}' has no launcher ID — use Standard deployment instead.");

        var gameDir = Path.GetDirectoryName(game.ExecutablePath)!;
        var gameExeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);

        // 1. Stage the DLLs before the game process exists (they must be on disk when the
        //    launcher spawns the game so EAC sees the correct d3d12/dxgi from the start).
        progress?.Report($"Staging {dlls.Count} layer(s) into {gameDir}...");
        var copied = await StageDllsAsync(gameDir, dlls);

        // 2. Fire the protocol URL.  The launcher validates the game binary and spawns it
        //    under its own trust chain, which satisfies EAC/BattlEye.
        var protocolUrl = BuildProtocolUrl(game);
        progress?.Report($"Firing launcher: {protocolUrl}");

        Process.Start(new ProcessStartInfo(protocolUrl) { UseShellExecute = true });

        // 3. Poll for the game process.  The launcher may take several seconds to validate
        //    the executable, patch overlays, and then spawn it.
        progress?.Report("Waiting for game process...");
        var gameProcess = await WaitForGameProcessAsync(gameExeName, timeoutMs: 120_000, ct);
        progress?.Report($"Attached to {gameExeName} (PID {gameProcess.Id}).");

        return (gameProcess, copied);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string BuildProtocolUrl(Game game)
    {
        return game.Source switch
        {
            "Steam" => $"steam://rungameid/{game.LauncherId}",
            "Epic"  => $"com.epicgames.launcher://apps/{game.LauncherId}?action=launch&silent=true",
            _       => throw new InvalidOperationException(
                            $"No protocol URL known for source '{game.Source}'.")
        };
    }

    private static async Task<Process> WaitForGameProcessAsync(
        string exeName, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        const int pollMs = 500;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var match = Process.GetProcessesByName(exeName)
                               .FirstOrDefault(p => !p.HasExited);
            if (match != null) return match;

            await Task.Delay(pollMs, ct);
        }

        throw new TimeoutException(
            $"Game process '{exeName}' did not appear within {timeoutMs / 1000}s. " +
            "The launcher may need longer, or the game name does not match the executable.");
    }

    // Copies GFXR DLLs into targetDir, backing up any existing file with the same name.
    // Returns the list of (deployed path, backup path) pairs for later cleanup.
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
