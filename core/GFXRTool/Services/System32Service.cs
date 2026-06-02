using System.Diagnostics;
using System.Security.Principal;

namespace GFXRTool.Services;

public class System32Service
{
    private static readonly string SysPath =
        Environment.GetFolderPath(Environment.SpecialFolder.System);

    private static readonly string[] ProxyDlls = { "d3d11", "d3d12", "dxgi" };
    private const string CaptureDll = "d3d12_capture";

    public enum Sys32Status { Original, CustomInstalled, BackupMissing, Mixed }

    public static bool IsAdmin()
    {
        var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ── Status ───────────────────────────────────────────────────────────────

    public Sys32Status GetStatus()
    {
        int backupsFound = 0;
        foreach (var dll in ProxyDlls)
            if (File.Exists(Path.Combine(SysPath, $"{dll}_ms.dll")))
                backupsFound++;

        return backupsFound switch
        {
            0 => Sys32Status.Original,
            3 => Sys32Status.CustomInstalled,
            _ => Sys32Status.Mixed
        };
    }

    public bool TestWriteAccess()
    {
        var tmp = Path.Combine(SysPath, "dx_write_test.tmp");
        try { File.WriteAllText(tmp, "test"); File.Delete(tmp); return true; }
        catch { return false; }
    }

    // ── Install ───────────────────────────────────────────────────────────────
    // Backs up the real DLLs as {name}_ms.dll, then copies in the GFXR layers.

    public async Task InstallAsync(string layersDir, IProgress<string>? progress = null)
    {
        RequireAdmin();

        progress?.Report("=== Installing GFXR layers to System32 ===");

        foreach (var dll in ProxyDlls)
        {
            var src    = Path.Combine(layersDir, $"{dll}.dll");
            var dst    = Path.Combine(SysPath,   $"{dll}.dll");
            var backup = Path.Combine(SysPath,   $"{dll}_ms.dll");

            progress?.Report($"Processing {dll}.dll...");

            if (!File.Exists(src))
            {
                progress?.Report($"  WARNING: {src} not found, skipping.");
                continue;
            }

            if (!File.Exists(backup))
            {
                await TakeOwnershipAsync(dst);
                try
                {
                    File.Move(dst, backup);
                    progress?.Report($"  Backup created: {dll}_ms.dll");
                }
                catch (Exception ex)
                {
                    progress?.Report($"  ERROR: Could not back up {dll}.dll — {ex.Message}");
                    continue;
                }
            }
            else
            {
                progress?.Report($"  Backup already exists.");
            }

            try
            {
                File.Copy(src, dst, overwrite: true);
                progress?.Report($"  Installed: {dll}.dll");
            }
            catch (Exception ex)
            {
                progress?.Report($"  ERROR: Failed to copy {dll}.dll — {ex.Message}");
            }
        }

        // d3d12_capture.dll has no original to back up — just copy it in
        var capSrc = Path.Combine(layersDir, $"{CaptureDll}.dll");
        var capDst = Path.Combine(SysPath,   $"{CaptureDll}.dll");

        if (File.Exists(capSrc))
        {
            try { File.Copy(capSrc, capDst, overwrite: true); progress?.Report($"  Installed: {CaptureDll}.dll"); }
            catch (Exception ex) { progress?.Report($"  ERROR: Failed to copy {CaptureDll}.dll — {ex.Message}"); }
        }
        else
        {
            progress?.Report($"  WARNING: {CaptureDll}.dll not found in layers dir.");
        }

        progress?.Report("=== Install complete ===");
    }

    // ── Restore ───────────────────────────────────────────────────────────────
    // Swaps the backups back and removes d3d12_capture.dll.

    public async Task RestoreAsync(IProgress<string>? progress = null)
    {
        RequireAdmin();

        progress?.Report("=== Restoring original DirectX DLLs ===");

        foreach (var dll in ProxyDlls)
        {
            var dst    = Path.Combine(SysPath, $"{dll}.dll");
            var backup = Path.Combine(SysPath, $"{dll}_ms.dll");

            progress?.Report($"Restoring {dll}.dll...");

            if (!File.Exists(backup))
            {
                progress?.Report($"  WARNING: No backup found for {dll}.dll.");
                continue;
            }

            await TakeOwnershipAsync(dst);

            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(backup, dst);
                progress?.Report($"  Restored: {dll}.dll");
            }
            catch (Exception ex)
            {
                progress?.Report($"  ERROR: Failed to restore {dll}.dll — {ex.Message}");
            }
        }

        var capDst = Path.Combine(SysPath, $"{CaptureDll}.dll");
        if (File.Exists(capDst))
        {
            try { File.Delete(capDst); progress?.Report($"  Removed: {CaptureDll}.dll"); }
            catch (Exception ex) { progress?.Report($"  ERROR: Could not remove {CaptureDll}.dll — {ex.Message}"); }
        }

        progress?.Report("=== Restore complete ===");
    }

    // ── Force Restore (SFC + DISM) ────────────────────────────────────────────
    // Last-resort recovery — repairs any corrupted system files.

    public async Task ForceRestoreAsync(IProgress<string>? progress = null)
    {
        RequireAdmin();

        progress?.Report("=== Force Restore: running SFC /scannow (may take several minutes) ===");
        await RunAsync("sfc.exe", "/scannow");
        progress?.Report("SFC complete.");

        progress?.Report("Running DISM /RestoreHealth...");
        await RunAsync("DISM.exe", "/Online /Cleanup-Image /RestoreHealth");
        progress?.Report("DISM complete. Force restore finished.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task TakeOwnershipAsync(string filePath)
    {
        await RunAsync("takeown", $"/F \"{filePath}\" /A");
        await RunAsync("icacls",  $"\"{filePath}\" /grant administrators:F");
    }

    private static async Task RunAsync(string exe, string args)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true
        });
        if (p != null) await p.WaitForExitAsync();
    }

    private static void RequireAdmin()
    {
        if (!IsAdmin())
            throw new InvalidOperationException(
                "System32 operations require administrator privileges. " +
                "Re-launch the tool as Administrator.");
    }
}
