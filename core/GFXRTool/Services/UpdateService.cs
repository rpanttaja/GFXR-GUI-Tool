using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GFXRTool.Services;

public class UpdateService
{
    private const string Repo        = "rpanttaja/GFXR-GUI-Tool";
    private const string ApiUrl      = $"https://api.github.com/repos/{Repo}/releases/latest";
    private const string VersionFile = "version.txt";

    private static readonly HttpClient Http = new();

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("GFXRTool-Updater");
    }

    public string InstalledVersion()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, VersionFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "(unknown)";
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        return await Http.GetFromJsonAsync<ReleaseInfo>(ApiUrl);
    }

    // Downloads the zip, extracts it, then hands off to a small PowerShell helper
    // that waits for this process to exit before copying files.
    // We can't overwrite GFXRTool.exe (or its DLLs) while they are loaded — the
    // helper runs as a separate process so Windows releases the file locks first.
    public async Task UpdateAndRestartAsync(ReleaseInfo release, IProgress<string> progress)
    {
        var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var zipAsset   = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException("No zip asset found in release.");

        // ── Download ──────────────────────────────────────────────────────────
        var tempZip = Path.Combine(Path.GetTempPath(), $"GFXRTool-{release.TagName}.zip");
        progress.Report($"Downloading {release.TagName}...");

        using (var response = await Http.GetAsync(zipAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(tempZip);
            await response.Content.CopyToAsync(fs);
        }

        progress.Report("Download complete. Extracting...");

        // ── Extract ───────────────────────────────────────────────────────────
        var tempDir = Path.Combine(Path.GetTempPath(), $"GFXRTool-extract-{release.TagName}");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        ZipFile.ExtractToDirectory(tempZip, tempDir);

        // Unwrap single top-level folder if present.
        var items       = Directory.GetFileSystemEntries(tempDir);
        var contentRoot = items.Length == 1 && Directory.Exists(items[0]) ? items[0] : tempDir;

        // ── Write the helper script ───────────────────────────────────────────
        // PowerShell script runs outside the app, waits for our PID to exit,
        // copies new files (skipping Layers), restores Layers, writes version.txt,
        // then relaunches GFXRTool.exe.
        var currentPid  = Environment.ProcessId;
        var layersDst   = Path.Combine(installDir, "Layers");
        var layersBackup = Path.Combine(Path.GetTempPath(), $"GFXRTool-Layers-{release.TagName}");
        var helperScript = Path.Combine(Path.GetTempPath(), "GFXRTool-update-helper.ps1");
        var newExe       = Path.Combine(installDir, "GFXRTool.exe");
        var versionDst   = Path.Combine(installDir, VersionFile);

        var ps = $"""
            $pid_to_wait = {currentPid}
            $src         = '{EscapePs(contentRoot)}'
            $dst         = '{EscapePs(installDir)}'
            $layersDst   = '{EscapePs(layersDst)}'
            $layersBak   = '{EscapePs(layersBackup)}'
            $versionFile = '{EscapePs(versionDst)}'
            $newVersion  = '{release.TagName}'
            $newExe      = '{EscapePs(newExe)}'
            $tempZip     = '{EscapePs(tempZip)}'
            $tempDir     = '{EscapePs(tempDir)}'

            # Wait for the app to fully release its file locks
            try {{
                $proc = Get-Process -Id $pid_to_wait -ErrorAction SilentlyContinue
                if ($proc) {{ $proc.WaitForExit(30000) | Out-Null }}
            }} catch {{ }}
            Start-Sleep -Milliseconds 500

            # Back up Layers so we don't overwrite the user's real capture DLLs
            if (Test-Path $layersDst) {{
                if (Test-Path $layersBak) {{ Remove-Item $layersBak -Recurse -Force }}
                Copy-Item $layersDst $layersBak -Recurse -Force
            }}

            # Copy all new files, skip Layers from the zip
            Get-ChildItem $src | Where-Object {{ $_.Name -ne 'Layers' }} | ForEach-Object {{
                $target = Join-Path $dst $_.Name
                if ($_.PSIsContainer) {{
                    Copy-Item $_.FullName $target -Recurse -Force
                }} else {{
                    Copy-Item $_.FullName $target -Force
                }}
            }}

            # Restore user's Layers
            if (Test-Path $layersBak) {{
                Copy-Item $layersBak $layersDst -Recurse -Force
                Remove-Item $layersBak -Recurse -Force -ErrorAction SilentlyContinue
            }}

            # Stamp new version
            $newVersion | Set-Content $versionFile

            # Cleanup temp files
            Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
            Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

            # Relaunch
            Start-Process $newExe
            """;

        File.WriteAllText(helperScript, ps);

        // ── Launch helper and exit ────────────────────────────────────────────
        progress.Report($"Handing off to update helper — restarting as {release.TagName}...");

        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helperScript}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });

        // Shut down — the helper is now watching our PID and will take over once we're gone.
        System.Windows.Application.Current.Dispatcher.Invoke(
            () => System.Windows.Application.Current.Shutdown());
    }

    private static string EscapePs(string path) => path.Replace("'", "''");

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class ReleaseInfo
    {
        [JsonPropertyName("tag_name")]  public string?        TagName { get; set; }
        [JsonPropertyName("assets")]    public List<Asset>?   Assets  { get; set; }
        [JsonPropertyName("html_url")]  public string?        HtmlUrl { get; set; }
    }

    public class Asset
    {
        [JsonPropertyName("name")]                   public string? Name        { get; set; }
        [JsonPropertyName("browser_download_url")]   public string? DownloadUrl { get; set; }
    }
}
