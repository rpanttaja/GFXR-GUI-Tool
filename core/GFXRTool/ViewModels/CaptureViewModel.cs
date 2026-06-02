using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GFXRTool.Services;
using System.Windows;

namespace GFXRTool.ViewModels;

public partial class CaptureViewModel : ObservableObject
{
    private readonly System.Diagnostics.Process _process;

    public string  GameName        { get; }
    public string  CaptureOutputDir { get; }
    public Action? RequestClose    { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusIndicatorText))]
    [NotifyPropertyChangedFor(nameof(StatusIndicatorColor))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TriggerCaptureCommand))]
    private bool _gameExited;

    [ObservableProperty]
    private string _statusMessage = "Ready to capture.";

    public string TriggerButtonText    => IsCapturing ? "■   Stop Capture"  : "▶   Trigger Capture";
    public string StatusIndicatorText  => IsCapturing ? "● Capturing"       : "● Standby";
    public string StatusIndicatorColor => IsCapturing ? "#F44747"           : "#858585";

    public CaptureViewModel(string gameName, string captureOutputDir,
                            System.Diagnostics.Process process)
    {
        GameName         = gameName;
        CaptureOutputDir = string.IsNullOrWhiteSpace(captureOutputDir)
                           ? "Default (game directory)"
                           : captureOutputDir;
        _process         = process;

        // Close the window automatically when the game exits.
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
        const ushort VK_F12 = 0x7B;
        var size = Marshal.SizeOf<INPUT>();

        NativeMethods.SendInput(1, [new INPUT
        {
            Type     = NativeMethods.INPUT_KEYBOARD,
            Keyboard = new KEYBDINPUT { Vk = VK_F12 }
        }], size);

        await Task.Delay(80);

        NativeMethods.SendInput(1, [new INPUT
        {
            Type     = NativeMethods.INPUT_KEYBOARD,
            Keyboard = new KEYBDINPUT { Vk = VK_F12, Flags = NativeMethods.KEYEVENTF_KEYUP }
        }], size);

        IsCapturing   = !IsCapturing;
        StatusMessage = IsCapturing ? "Capturing..." : "Capture stopped.";
    }

    private bool CanTrigger() => !GameExited;
}
