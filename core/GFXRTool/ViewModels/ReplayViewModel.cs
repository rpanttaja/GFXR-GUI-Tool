using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;

namespace GFXRTool.ViewModels;

public partial class ReplayViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartReplayCommand))]
    private string _captureFilePath = string.Empty;

    [ObservableProperty]
    private string _replayExePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Select a .gfxr capture file and click Start Replay.";

    [ObservableProperty]
    private bool _isReplaying;

    [ObservableProperty]
    private string _extraArgs = string.Empty;

    public ReplayViewModel()
    {
        // Auto-locate gfxrecon-replay.exe next to the running exe.
        var baseDir     = AppDomain.CurrentDomain.BaseDirectory;
        var defaultExe  = Path.Combine(baseDir, "gfxrecon-replay.exe");
        if (File.Exists(defaultExe))
            ReplayExePath = defaultExe;
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

    [RelayCommand]
    private void BrowseReplayExe()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Locate gfxrecon-replay.exe",
            Filter = "Executable Files (*.exe)|*.exe"
        };
        if (dlg.ShowDialog() == true)
            ReplayExePath = dlg.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private async Task StartReplayAsync()
    {
        if (!File.Exists(ReplayExePath))
        {
            StatusMessage = "gfxrecon-replay.exe not found — use Browse to locate it.";
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
                FileName         = ReplayExePath,
                Arguments        = args,
                WorkingDirectory = Path.GetDirectoryName(ReplayExePath),
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
