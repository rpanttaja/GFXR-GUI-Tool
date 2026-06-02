using System.Windows;

namespace GFXRTool;

public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Left = area.Right  - Width  - 16;
        Top  = area.Bottom - Height - 16;
    }

    public void Log(string message) =>
        Dispatcher.InvokeAsync(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}]  {message}\n");
            LogBox.ScrollToEnd();
        });

    private void Clear_Click(object sender, RoutedEventArgs e)   => LogBox.Clear();
    private void CopyAll_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(string.IsNullOrEmpty(LogBox.Text) ? "" : LogBox.Text);
}
