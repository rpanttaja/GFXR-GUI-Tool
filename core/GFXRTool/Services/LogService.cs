using System.Text;
using System.Windows;

namespace GFXRTool.Services;

public class LogService
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public string LogPath => _logPath;

    public LogService()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        _logPath = Path.Combine(dir, "gfxrtool.log");

        // Overwrite on each launch so the file always reflects the latest session.
        try
        {
            File.WriteAllText(_logPath,
                $"GFXR Tool Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"{new string('-', 60)}{Environment.NewLine}");
        }
        catch { /* if we can't write, silently continue */ }
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_lock)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); }
            catch { }
        }
    }

    public void LogError(string context, Exception ex)
    {
        Log($"ERROR [{context}] {ex.GetType().Name}: {ex.Message}");
        if (ex.StackTrace is string st)
            Log($"  StackTrace: {st.ReplaceLineEndings($"{Environment.NewLine}  ")}");
        if (ex.InnerException is Exception inner)
            Log($"  InnerException: {inner.GetType().Name}: {inner.Message}");
    }

    public void CopyToClipboard()
    {
        // Clipboard must be called on the STA/UI thread.
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var text = File.ReadAllText(_logPath);
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Clipboard.SetText($"(could not read log file: {ex.Message})");
            }
        });
    }
}
