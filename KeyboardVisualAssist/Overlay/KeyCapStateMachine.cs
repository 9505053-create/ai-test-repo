using System.Windows.Threading;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// 鍵帽視覺狀態機
/// 
/// 分流規則：
///   修飾鍵（IsModifier = true）
///     KeyDown → Pressed
///     KeyUp   → Normal
///     無 Fading，直接同步實體狀態
///
///   一般鍵（IsModifier = false）
///     KeyDown → Pressed
///     KeyUp   → Fading（啟動獨立計時器）→ Normal
///     每鍵有獨立 Timer，互不干擾
///
/// 責任：
///   - 控制每個 KeyCapViewModel 的 State / IsPressed
///   - 管理 Fading 計時器生命週期
///   - 不直接觸碰 ObservableCollection 或 UI 元素
/// </summary>
public class KeyCapStateMachine : IDisposable
{
    // Fading 持續時間（ms），可由外部注入
    private readonly int _fadeDurationMs;

    // 每個一般鍵的獨立 Fade Timer，key = KeyId
    private readonly Dictionary<string, DispatcherTimer> _fadeTimers = new();

    public KeyCapStateMachine(int fadeDurationMs = 300)
    {
        _fadeDurationMs = fadeDurationMs;
    }

    // ── 修飾鍵事件 ───────────────────────────────────────

    /// <summary>
    /// 修飾鍵狀態同步：直接對應實體按下/放開，無 Fade。
    /// </summary>
    public void OnModifierChanged(KeyCapViewModel cap, bool isDown)
    {
        cap.IsPressed = isDown;
        // DisplayState 由 KeyCapViewModel 內部根據 IsModifier + IsPressed 計算
    }

    // ── 一般鍵事件 ───────────────────────────────────────

    /// <summary>
    /// 一般鍵按下：設為 Pressed。
    /// </summary>
    public void OnNormalKeyDown(KeyCapViewModel cap)
    {
        // 若該鍵還在 Fading 計時中，先取消
        CancelFadeTimer(cap.KeyId);
        cap.State = KeyCapState.Pressed;
    }

    /// <summary>
    /// 一般鍵放開：進入 Fading，計時結束後回 Normal。
    /// </summary>
    public void OnNormalKeyUp(KeyCapViewModel cap)
    {
        // 只有 Pressed 狀態才需要 Fading；若已是 Normal 直接跳過
        if (cap.State != KeyCapState.Pressed) return;

        cap.State = KeyCapState.Fading;
        StartFadeTimer(cap);
    }

    // ── 清除 ─────────────────────────────────────────────

    /// <summary>
    /// 清除所有一般鍵高亮（手動清除按鈕用）。
    /// 修飾鍵狀態由 HighlightModifiers 管理，這裡不動。
    /// </summary>
    public void ClearAllNormalKeys(IEnumerable<KeyCapViewModel> caps)
    {
        // 先停所有計時器
        foreach (var timer in _fadeTimers.Values) timer.Stop();
        _fadeTimers.Clear();

        foreach (var cap in caps)
        {
            if (!cap.IsModifier)
                cap.State = KeyCapState.Normal;
        }
    }

    /// <summary>
    /// 清除全部（含修飾鍵），手動清除時呼叫。
    /// </summary>
    public void ClearAll(IEnumerable<KeyCapViewModel> caps)
    {
        foreach (var timer in _fadeTimers.Values) timer.Stop();
        _fadeTimers.Clear();

        foreach (var cap in caps)
        {
            cap.State = KeyCapState.Normal;
            cap.IsPressed = false;
        }
    }

    // ── 計時器管理 ───────────────────────────────────────

    private void StartFadeTimer(KeyCapViewModel cap)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(_fadeDurationMs)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _fadeTimers.Remove(cap.KeyId);
            cap.State = KeyCapState.Normal;
        };

        _fadeTimers[cap.KeyId] = timer;
        timer.Start();
    }

    private void CancelFadeTimer(string keyId)
    {
        if (_fadeTimers.TryGetValue(keyId, out var timer))
        {
            timer.Stop();
            _fadeTimers.Remove(keyId);
        }
    }

    // ── 釋放 ─────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var timer in _fadeTimers.Values) timer.Stop();
        _fadeTimers.Clear();
    }
}
