using System.IO;

namespace KeyboardVisualAssist.Logging;

/// <summary>
/// 簡易 File Logger
/// 安全原則：只記錄錯誤與狀態，絕對不記錄任何輸入內容
/// </summary>
public static class AppLogger
{
    private static string _logPath = "";
    private static readonly object _lock = new();

    public static void Init()
    {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, $"kva_{DateTime.Now:yyyyMMdd}.log");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message);
        if (ex != null)
            Write("ERROR", $"  Exception: {ex.GetType().Name}: {ex.Message}");
    }

    private static void Write(string level, string message)
    {
        if (string.IsNullOrEmpty(_logPath)) return;

        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            lock (_lock)
                File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch
        {
            // Logger 不能因為自身錯誤而讓應用程式崩潰
        }
    }
}
