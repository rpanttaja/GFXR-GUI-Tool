using GFXRTool.ViewModels;
using System.Windows;

namespace GFXRTool;

public partial class CaptureWindow : Window
{
    public CaptureWindow(CaptureViewModel vm)
    {
        InitializeComponent();
        DataContext    = vm;
        vm.RequestClose = Close;
    }
}
