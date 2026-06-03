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

    [DllImport("user32.dll")] private static extern long   GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern long   SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong);
    [DllImport("user32.dll")] private static extern bool   SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool   GetCursorPos(out POINT pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private bool  _dragging;
    private POINT _dragStart;      // screen coords where drag began
    private double _winLeftStart;  // window Left when drag began
    private double _winTopStart;   // window Top when drag began

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

    // Called from the Border's MouseLeftButtonDown (wired in XAML).
    internal void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out _dragStart)) return;
        _winLeftStart = Left;
        _winTopStart  = Top;
        _dragging     = true;
        // Capture to the border element so we get moves even if cursor leaves it.
        ((IInputElement)sender).CaptureMouse();
        e.Handled = true;
    }

    internal void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (!GetCursorPos(out var cur)) return;

        var newLeft = _winLeftStart + (cur.X - _dragStart.X);
        var newTop  = _winTopStart  + (cur.Y - _dragStart.Y);

        var hwnd = new WindowInteropHelper(this).Handle;
        // Move without activating.
        SetWindowPos(hwnd, IntPtr.Zero, (int)newLeft, (int)newTop, 0, 0,
                     SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        // Keep WPF Left/Top in sync so subsequent reads are correct.
        Left = newLeft;
        Top  = newTop;
    }

    internal void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ((IInputElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }
}
