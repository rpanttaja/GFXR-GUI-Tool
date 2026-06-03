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

    private void OnMouseDown(object sender, MouseButtonEventArgs e) => DragMove();
}
