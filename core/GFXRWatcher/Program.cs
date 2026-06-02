using System.Diagnostics;

if (args.Length < 1 || !int.TryParse(args[0], out int gamePid))
{
    Console.Error.WriteLine("Usage: GFXRWatcher <gamePid>");
    return 1;
}

// Wait for the game process to exit before restoring.
// If the process is already gone (e.g. GFXRTool was killed after the game exited),
// ArgumentException is caught and we fall through to restore anyway.
try
{
    var game = Process.GetProcessById(gamePid);
    await game.WaitForExitAsync();
}
catch (ArgumentException) { }

await RestoreSystem32Async();
return 0;

// ---------------------------------------------------------------------------

static async Task RestoreSystem32Async()
{
    var sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string[] proxyDlls = ["d3d11", "d3d12", "dxgi"];
    const string captureDll = "d3d12_capture";

    foreach (var dll in proxyDlls)
    {
        var dst    = Path.Combine(sysPath, $"{dll}.dll");
        var backup = Path.Combine(sysPath, $"{dll}_ms.dll");

        if (!File.Exists(backup)) continue;

        await TakeOwnershipAsync(dst);
        try
        {
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(backup, dst);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to restore {dll}.dll: {ex.Message}");
        }
    }

    var capDst = Path.Combine(sysPath, $"{captureDll}.dll");
    if (File.Exists(capDst))
        try { File.Delete(capDst); } catch { }
}

static async Task TakeOwnershipAsync(string filePath)
{
    await RunAsync("takeown", $"/F \"{filePath}\" /A");
    await RunAsync("icacls",  $"\"{filePath}\" /grant administrators:F");
}

static async Task RunAsync(string exe, string args)
{
    var p = Process.Start(new ProcessStartInfo(exe, args)
    {
        UseShellExecute        = false,
        CreateNoWindow         = true,
        RedirectStandardOutput = true,
    });
    if (p != null) await p.WaitForExitAsync();
}
