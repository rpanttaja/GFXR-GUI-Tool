using GFXRTool.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace GFXRTool;

public partial class OverlayWindow : Window
{
    public OverlayWindow(CaptureViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Left = SystemParameters.WorkArea.Right - ActualWidth - 12;
        Top  = SystemParameters.WorkArea.Top   + 12;
    }

    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // Stop the mouse-down from bubbling up to the drag handler when the button is clicked.
    private void OnButtonMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
