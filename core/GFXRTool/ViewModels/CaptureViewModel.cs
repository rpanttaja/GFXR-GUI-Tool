using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GFXRTool.Services;
using System.Windows;

namespace GFXRTool.ViewModels;

public partial class CaptureViewModel : ObservableObject
{
    private readonly System.Diagnostics.Process _process;

    public string  GameName         { get; }
    public string  CaptureOutputDir { get; }
    public string  TriggerKey       { get; }
    public Action? RequestClose     { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusIndicatorText))]
    [NotifyPropertyChangedFor(nameof(StatusIndicatorColor))]
    [NotifyPropertyChangedFor(nameof(CaptureStatusText))]
    [NotifyPropertyChangedFor(nameof(CaptureStatusColor))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TriggerCaptureCommand))]
    private bool _gameExited;

    [ObservableProperty]
    private string _statusMessage = "Ready to capture.";

    public string TriggerButtonText =>
        IsCapturing
            ? $"■   Stop Capture  ({TriggerKey})"
            : $"▶   Start Capture  ({TriggerKey})";

    public string StatusIndicatorText  => IsCapturing ? "● CAPTURING" : "● STANDBY";
    public string StatusIndicatorColor => IsCapturing ? "#F44747"     : "#858585";

    // Larger, prominent status shown in the middle of the trigger tab
    public string CaptureStatusText    => IsCapturing ? "CAPTURE IN PROGRESS" : "READY";
    public string CaptureStatusColor   => IsCapturing ? "#F44747"              : "#4EC9B0";

    public CaptureViewModel(string gameName, string captureOutputDir,
                            string triggerKey,
                            System.Diagnostics.Process process)
    {
        GameName         = gameName;
        TriggerKey       = triggerKey;
        CaptureOutputDir = string.IsNullOrWhiteSpace(captureOutputDir)
                           ? "Default (game directory)"
                           : captureOutputDir;
        _process         = process;

        _ = _process.WaitForExitAsync().ContinueWith(_ =>
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                GameExited    = true;
                IsCapturing   = false;
                StatusMessage = "Game exited.";
                RequestClose?.Invoke();
            }));
    }

    [RelayCommand(CanExecute = nameof(CanTrigger))]
    private async Task TriggerCaptureAsync()
    {
        if (!NativeMethods.TriggerKeyVk.TryGetValue(TriggerKey, out var vk))
            vk = 0x7B; // fallback F12

        var size = Marshal.SizeOf<INPUT>();

        NativeMethods.SendInput(1, [new INPUT
        {
            Type     = NativeMethods.INPUT_KEYBOARD,
            Keyboard = new KEYBDINPUT { Vk = vk }
        }], size);

        await Task.Delay(80);

        NativeMethods.SendInput(1, [new INPUT
        {
            Type     = NativeMethods.INPUT_KEYBOARD,
            Keyboard = new KEYBDINPUT { Vk = vk, Flags = NativeMethods.KEYEVENTF_KEYUP }
        }], size);

        IsCapturing   = !IsCapturing;
        StatusMessage = IsCapturing ? $"Capturing...  ({TriggerKey} to stop)" : "Capture stopped.";
    }

    private bool CanTrigger() => !GameExited;
}
