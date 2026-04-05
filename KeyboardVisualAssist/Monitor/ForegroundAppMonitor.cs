using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Monitor;

/// <summary>
/// 定期偵測前景視窗的程序名稱
/// 只有在指定 App 前景時，才顯示 Overlay
/// </summary>
public class ForegroundAppMonitor
{
    #region Win32 API

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    #endregion

    private readonly HashSet<string> _targetApps;
    private readonly DispatcherTimer _timer;
    private bool _lastVisible = true;

    /// <summary>前景 App 變化時觸發，bool = 是否應顯示 overlay</summary>
    public event Action<bool>? AppChanged;

    public ForegroundAppMonitor(List<string> targetApps)
    {
        // 統一轉小寫，比對時不分大小寫
        _targetApps = targetApps
            .Select(a => a.ToLowerInvariant())
            .ToHashSet();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                SetVisible(false);
                return;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                SetVisible(false);
                return;
            }

            using var process = Process.GetProcessById((int)pid);
            var procName = process.ProcessName.ToLowerInvariant() + ".exe";
            var procNameOnly = process.ProcessName.ToLowerInvariant();

            bool isTarget = _targetApps.Contains(procName) || _targetApps.Contains(procNameOnly);
            SetVisible(isTarget);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ForegroundAppMonitor 偵測失敗", ex);
        }
    }

    private void SetVisible(bool visible)
    {
        if (visible == _lastVisible) return;
        _lastVisible = visible;
        AppLogger.Info($"Overlay 顯示狀態切換: {visible}");
        AppChanged?.Invoke(visible);
    }
}
