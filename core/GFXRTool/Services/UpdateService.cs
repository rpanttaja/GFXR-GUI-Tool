using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GFXRTool.Services;

public class UpdateService
{
    private const string Repo    = "rpanttaja/GFXR-GUI-Tool";
    private const string ApiUrl  = $"https://api.github.com/repos/{Repo}/releases/latest";
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

    // Downloads the latest release zip, extracts it over the current install dir,
    // preserves the Layers folder, writes version.txt, then restarts the app.
    public async Task UpdateAndRestartAsync(ReleaseInfo release, IProgress<string> progress)
    {
        var installDir = AppDomain.CurrentDomain.BaseDirectory;
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

        progress.Report("Download complete. Preparing to install...");

        // ── Extract to a temp dir ─────────────────────────────────────────────
        var tempDir = Path.Combine(Path.GetTempPath(), $"GFXRTool-extract-{release.TagName}");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempDir);

        // The zip may contain a single top-level folder — unwrap it.
        var items       = Directory.GetFileSystemEntries(tempDir);
        var contentRoot = items.Length == 1 && Directory.Exists(items[0]) ? items[0] : tempDir;

        // ── Preserve Layers ───────────────────────────────────────────────────
        var layersDst    = Path.Combine(installDir, "Layers");
        var layersBackup = Path.Combine(Path.GetTempPath(), $"GFXRTool-Layers-{release.TagName}");

        if (Directory.Exists(layersDst))
        {
            progress.Report("Preserving Layers folder...");
            if (Directory.Exists(layersBackup)) Directory.Delete(layersBackup, recursive: true);
            CopyDirectory(layersDst, layersBackup);
        }

        // ── Copy new files, skip Layers ───────────────────────────────────────
        progress.Report("Installing update...");
        foreach (var entry in new DirectoryInfo(contentRoot).EnumerateFileSystemInfos())
        {
            if (entry.Name.Equals("Layers", StringComparison.OrdinalIgnoreCase)) continue;

            var dst = Path.Combine(installDir, entry.Name);
            if (entry is DirectoryInfo di)
                CopyDirectory(di.FullName, dst);
            else
                File.Copy(entry.FullName, dst, overwrite: true);
        }

        // ── Restore Layers ────────────────────────────────────────────────────
        if (Directory.Exists(layersBackup))
        {
            progress.Report("Restoring Layers folder...");
            CopyDirectory(layersBackup, layersDst);
        }

        // ── Write version stamp ───────────────────────────────────────────────
        File.WriteAllText(Path.Combine(installDir, VersionFile), release.TagName);

        // ── Cleanup ───────────────────────────────────────────────────────────
        try { File.Delete(tempZip); } catch { }
        try { Directory.Delete(tempDir,    recursive: true); } catch { }
        try { Directory.Delete(layersBackup, recursive: true); } catch { }

        // ── Restart ───────────────────────────────────────────────────────────
        progress.Report($"Update to {release.TagName} complete. Restarting...");

        var exe = Path.Combine(installDir, "GFXRTool.exe");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current.Dispatcher.Invoke(
            () => System.Windows.Application.Current.Shutdown());
    }

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
