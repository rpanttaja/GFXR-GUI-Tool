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

        // Replay\ subfolder is copied from core/Replay on build and contains the exe plus
        // all required DLLs (dxcompiler.dll, D3D12\) for the x64 release.
        var candidates = new[]
        {
            Path.Combine(baseDir, "Replay", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "..", "Replay", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "..", "..", "..", "Replay", "gfxrecon-replay.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "GFXReconstruct_Windows_x64_Release", "tools", "windows", "x64", "gfxrecon-replay.exe"),
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
            StatusMessage = "gfxrecon-replay.exe not found — ensure the Replay folder is next to the tool (built automatically on build).";
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
