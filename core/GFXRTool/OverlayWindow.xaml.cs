using GFXRTool.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GFXRTool;

public partial class OverlayWindow : Window
{
    private const int  GWL_EXSTYLE      = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const uint SWP_NOACTIVATE   = 0x0010;
    private const uint SWP_NOZORDER     = 0x0004;
    private const uint SWP_NOSIZE       = 0x0001;

    [DllImport("user32.dll")] private static extern long GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern long SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }

    private bool   _dragging;
    private POINT  _cursorStart;
    private int    _winLeftStart;
    private int    _winTopStart;

    public OverlayWindow(CaptureViewModel vm)
    {
        InitializeComponent();
        DataContext   = vm;
        ShowActivated = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd  = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        Left = SystemParameters.WorkArea.Right - ActualWidth - 12;
        Top  = SystemParameters.WorkArea.Top   + 12;
    }

    internal void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out _cursorStart)) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out var rc)) return;

        _winLeftStart = rc.Left;
        _winTopStart  = rc.Top;
        _dragging     = true;
        ((IInputElement)sender).CaptureMouse();
        e.Handled = true;
    }

    internal void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || !GetCursorPos(out var cur)) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        // Move purely at the Win32 level — never touch WPF Left/Top mid-drag
        // because that triggers a layout pass which calls SetWindowPos again
        // and causes jitter.
        SetWindowPos(hwnd, IntPtr.Zero,
                     _winLeftStart + (cur.X - _cursorStart.X),
                     _winTopStart  + (cur.Y - _cursorStart.Y),
                     0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    internal void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ((IInputElement)sender).ReleaseMouseCapture();

        // Sync WPF Left/Top once from the real window position so the
        // values are correct if the window is repositioned again later.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (GetWindowRect(hwnd, out var rc))
        {
            Left = rc.Left;
            Top  = rc.Top;
        }
        e.Handled = true;
    }
}
