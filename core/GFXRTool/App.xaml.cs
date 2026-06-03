using GFXRTool.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace GFXRTool;

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public partial class App : Application
{
    // Shared log used by the crash handlers before MainViewModel is constructed.
    internal static readonly LogService StartupLog = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Unhandled exceptions on UI thread
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Unhandled exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Unhandled exceptions from async Task continuations that were never awaited
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupLog.Log($"UNHANDLED UI EXCEPTION (DispatcherUnhandledException)");
        StartupLog.LogError("UI thread", e.Exception);
        e.Handled = true; // keep the app alive so the log gets flushed

        MessageBox.Show(
            $"Unhandled error:\n\n{e.Exception.Message}\n\nLog written to:\n{StartupLog.LogPath}",
            "GFXR Tool — Crash", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            StartupLog.Log($"UNHANDLED EXCEPTION (AppDomain)  isTerminating={e.IsTerminating}");
            StartupLog.LogError("AppDomain", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupLog.Log("UNOBSERVED TASK EXCEPTION");
        StartupLog.LogError("TaskScheduler", e.Exception);
        e.SetObserved(); // prevent process teardown
    }
}
