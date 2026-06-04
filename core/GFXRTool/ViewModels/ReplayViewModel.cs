using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace GFXRTool.ViewModels;

public partial class ReplayViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartReplayCommand))]
    private string _captureFilePath = string.Empty;

    private readonly string _replayExePath;

    [ObservableProperty]
    private string _statusMessage = "Select a .gfxr capture file and click Start Replay.";

    [ObservableProperty]
    private bool _isReplaying;

    [ObservableProperty]
    private string _extraArgs = string.Empty;

    public ReplayViewModel()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Prefer the full GFXReconstruct release directory (ships with all required DLLs).
        // Standalone Replay\ copies lack DLLs and fail with STATUS_DLL_NOT_FOUND (0xC0000135).
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "GFXReconstruct_Windows_arm64_Release", "tools", "windows", "arm64", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "Replay", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "..", "Replay", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "..", "..", "..", "Replay", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Replay", "gfxrecon-replay.exe"),
        };

        _replayExePath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? string.Empty;
    }

    [RelayCommand]
    private void BrowseCaptureFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select GFXR Capture File",
            Filter = "GFXR Capture Files (*.gfxr)|*.gfxr|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            CaptureFilePath = dlg.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task StartReplayAsync()
    {
        if (!File.Exists(_replayExePath))
        {
            StatusMessage = "gfxrecon-replay.exe not found — ensure the Replay folder is next to the tool.";
            return;
        }

        IsReplaying   = true;
        StatusMessage = $"Replaying: {Path.GetFileName(CaptureFilePath)}";

        try
        {
            var args = $"\"{CaptureFilePath}\"";
            if (!string.IsNullOrWhiteSpace(ExtraArgs))
                args += " " + ExtraArgs.Trim();

            var psi = new ProcessStartInfo
            {
                FileName         = _replayExePath,
                Arguments        = args,
                WorkingDirectory = Path.GetDirectoryName(_replayExePath),
                UseShellExecute  = false,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start gfxrecon-replay.exe.");

            await proc.WaitForExitAsync();

            StatusMessage = proc.ExitCode == 0
                ? "Replay finished."
                : $"Replay exited with code {proc.ExitCode}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Replay failed: {ex.Message}";
        }
        finally
        {
            IsReplaying = false;
        }
    }

    private bool CanReplay() => !string.IsNullOrWhiteSpace(CaptureFilePath);
}
