using GFXRTool.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GFXRTool;

public partial class OverlayWindow : Window
{
    private const int  GWL_EXSTYLE       = -20;
    private const long WS_EX_NOACTIVATE  = 0x08000000L;
    private const long WS_EX_TOOLWINDOW  = 0x00000080L;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern long SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong);

    public OverlayWindow(CaptureViewModel vm)
    {
        InitializeComponent();
        DataContext    = vm;
        ShowActivated  = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        // NOACTIVATE = never steals focus; TOOLWINDOW = hidden from Alt+Tab
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        Left = SystemParameters.WorkArea.Right - ActualWidth - 12;
        Top  = SystemParameters.WorkArea.Top   + 12;
    }
}
