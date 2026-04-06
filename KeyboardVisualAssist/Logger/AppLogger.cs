using System.IO;
using System.Text;

namespace KeyboardVisualAssist.Logging;

/// <summary>
/// File Logger v1.2
/// - 每次執行產生新的帶時間戳 LOG 檔
/// - 保留最近 7 個 LOG 檔，自動清理舊檔
/// - 安全原則：只記錄狀態/錯誤，不記錄任何按鍵輸入內容
/// </summary>
public static class AppLogger
{
    private static string _logPath = "";
    private static readonly object _lock = new();
    private const int KeepLogCount = 7;

    public static string CurrentLogPath => _logPath;

    public static void Init()
    {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        // 每次執行用精確時間戳建立新檔，方便 debug
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logPath = Path.Combine(logDir, $"kva_{timestamp}.log");

        // 寫 session header
        var header = new StringBuilder();
        header.AppendLine("=".PadRight(60, '='));
        header.AppendLine($"  KeyboardVisualAssist Session Log");
        header.AppendLine($"  Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        header.AppendLine($"  OS:    {Environment.OSVersion}");
        header.AppendLine($"  .NET:  {Environment.Version}");
        header.AppendLine($"  Log:   {_logPath}");
        header.AppendLine("=".PadRight(60, '='));

        File.WriteAllText(_logPath, header.ToString(), Encoding.UTF8);

        // 清理舊 LOG
        CleanOldLogs(logDir);

        Info("Logger 初始化完成");
    }

    public static void Info(string message)  => Write("INFO ", message);
    public static void Warn(string message)  => Write("WARN ", message);
    public static void Debug(string message) => Write("DEBUG", message);

    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message);
        if (ex != null)
        {
            Write("ERROR", $"  Type:    {ex.GetType().FullName}");
            Write("ERROR", $"  Message: {ex.Message}");
            if (ex.StackTrace != null)
            {
                foreach (var line in ex.StackTrace.Split('\n').Take(5))
                    Write("ERROR", $"  Stack:   {line.Trim()}");
            }
            if (ex.InnerException != null)
                Write("ERROR", $"  Inner:   {ex.InnerException.Message}");
        }
    }

    private static void Write(string level, string message)
    {
        if (string.IsNullOrEmpty(_logPath)) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (_lock)
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* Logger 不能讓應用程式崩潰 */ }
    }

    private static void CleanOldLogs(string logDir)
    {
        try
        {
            var logs = Directory.GetFiles(logDir, "kva_*.log")
                                .OrderByDescending(f => f)
                                .Skip(KeepLogCount)
                                .ToArray();
            foreach (var old in logs)
            {
                File.Delete(old);
                Debug($"清理舊 LOG: {Path.GetFileName(old)}");
            }
        }
        catch { /* 清理失敗不影響運作 */ }
    }
}
