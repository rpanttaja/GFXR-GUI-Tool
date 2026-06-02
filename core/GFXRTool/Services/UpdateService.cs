using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GFXRTool.Services;

public class UpdateService
{
    private const string Repo        = "rpanttaja/GFXR-GUI-Tool";
    private const string ApiUrl      = $"https://api.github.com/repos/{Repo}/releases/latest";
    // Source zip URL — always points to the latest main branch source
    private const string SourceZipUrl = $"https://github.com/{Repo}/archive/refs/heads/main.zip";
    private const string VersionFile = "version.txt";

    private static readonly HttpClient Http = new();

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("GFXRTool-Updater");
    }

    public string InstalledVersion()
    {
        // Check version.txt next to the exe first, then walk up to the repo root.
        foreach (var dir in AncestorDirs(AppDomain.CurrentDomain.BaseDirectory).Take(5))
        {
            var f = Path.Combine(dir, VersionFile);
            if (File.Exists(f)) return File.ReadAllText(f).Trim();
        }
        return "(unknown)";
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        return await Http.GetFromJsonAsync<ReleaseInfo>(ApiUrl);
    }

    // Downloads the latest source zip from GitHub, extracts it over the install root,
    // preserves the Layers folder, writes version.txt, then relaunches via run.bat.
    // All file operations happen in a PowerShell helper that runs after this process exits
    // so no file locks block the copy.
    public async Task UpdateAndRestartAsync(ReleaseInfo release, IProgress<string> progress)
    {
        // Determine the repo root — walk up from BaseDirectory until we find run.bat
        var repoRoot = FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory)
                       ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

        progress.Report($"Downloading latest source ({release.TagName})...");

        var tempZip = Path.Combine(Path.GetTempPath(), $"GFXR-GUI-Tool-{release.TagName}.zip");

        using (var response = await Http.GetAsync(SourceZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(tempZip);
            await response.Content.CopyToAsync(fs);
        }

        progress.Report("Download complete. Preparing helper...");

        var tempDir      = Path.Combine(Path.GetTempPath(), $"GFXR-source-{release.TagName}");
        var helperScript = Path.Combine(Path.GetTempPath(), "GFXRTool-update-helper.ps1");
        var layersSrc    = Path.Combine(repoRoot, "core", "Layers");
        var layersBak    = Path.Combine(Path.GetTempPath(), $"GFXRTool-Layers-{release.TagName}");
        var runBat       = Path.Combine(repoRoot, "Launch GFXR Tool.bat");
        var versionDst   = Path.Combine(repoRoot, VersionFile);
        var currentPid   = Environment.ProcessId;

        // The GitHub archive zip contains a single top-level folder named
        // "GFXR-GUI-Tool-main" — we strip it and copy contents into repoRoot.
        var ps = $$"""
            $pid_to_wait = {{currentPid}}
            $tempZip     = '{{EscapePs(tempZip)}}'
            $tempDir     = '{{EscapePs(tempDir)}}'
            $repoRoot    = '{{EscapePs(repoRoot)}}'
            $layersSrc   = '{{EscapePs(layersSrc)}}'
            $layersBak   = '{{EscapePs(layersBak)}}'
            $runBat      = '{{EscapePs(runBat)}}'
            $versionFile = '{{EscapePs(versionDst)}}'
            $newVersion  = '{{release.TagName}}'

            # Wait for GFXRTool to fully exit
            try {
                $proc = Get-Process -Id $pid_to_wait -ErrorAction SilentlyContinue
                if ($proc) { $proc.WaitForExit(30000) | Out-Null }
            } catch { }
            Start-Sleep -Milliseconds 800

            # Expand zip
            if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
            Expand-Archive -Path $tempZip -DestinationPath $tempDir

            # GitHub archive has one top-level folder — unwrap it
            $inner = Get-ChildItem $tempDir | Where-Object { $_.PSIsContainer } | Select-Object -First 1
            $src   = if ($inner) { $inner.FullName } else { $tempDir }

            # Back up Layers
            if (Test-Path $layersSrc) {
                if (Test-Path $layersBak) { Remove-Item $layersBak -Recurse -Force }
                Copy-Item $layersSrc $layersBak -Recurse -Force
            }

            # Copy new source over repo root, skip Layers subfolder from zip
            Get-ChildItem $src | ForEach-Object {
                $target = Join-Path $repoRoot $_.Name
                if ($_.PSIsContainer) {
                    # Don't overwrite Layers with what's in the zip
                    if ($_.Name -eq 'core') {
                        Get-ChildItem $_.FullName | Where-Object { $_.Name -ne 'Layers' } | ForEach-Object {
                            $t = Join-Path $target $_.Name
                            if ($_.PSIsContainer) { Copy-Item $_.FullName $t -Recurse -Force }
                            else { Copy-Item $_.FullName $t -Force }
                        }
                    } else {
                        Copy-Item $_.FullName $target -Recurse -Force
                    }
                } else {
                    Copy-Item $_.FullName $target -Force
                }
            }

            # Restore Layers
            if (Test-Path $layersBak) {
                Copy-Item $layersBak $layersSrc -Recurse -Force
                Remove-Item $layersBak -Recurse -Force -ErrorAction SilentlyContinue
            }

            # Stamp version
            $newVersion | Set-Content $versionFile

            # Cleanup
            Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
            Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

            # Relaunch via run.bat (rebuilds and starts the tool)
            Start-Process $runBat
            """;

        File.WriteAllText(helperScript, ps);

        progress.Report($"Handing off to update helper — rebuilding {release.TagName}...");

        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helperScript}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });

        System.Windows.Application.Current.Dispatcher.Invoke(
            () => System.Windows.Application.Current.Shutdown());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindRepoRoot(string startDir)
    {
        foreach (var dir in AncestorDirs(startDir))
            if (File.Exists(Path.Combine(dir, "Launch GFXR Tool.bat")))
                return dir;
        return null;
    }

    private static IEnumerable<string> AncestorDirs(string start)
    {
        var dir = Path.GetFullPath(start).TrimEnd('\\', '/');
        while (!string.IsNullOrEmpty(dir))
        {
            yield return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) yield break;
            dir = parent ?? string.Empty;
        }
    }

    private static string EscapePs(string path) => path.Replace("'", "''");

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
