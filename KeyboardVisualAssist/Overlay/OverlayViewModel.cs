using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using KeyboardVisualAssist.Config;
using KeyboardVisualAssist.InputCapture;
using KeyboardVisualAssist.KeyMap;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// Overlay 視窗的 ViewModel v1.1
/// - 持有 KeyCaps 集合（取代舊 LayoutEntries + HighlightedKeyIds 雙清單）
/// - 修飾鍵與一般鍵分開高亮邏輯
/// - 支援 Compact/Full 視圖切換
/// </summary>
public partial class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private readonly KeyMapper _mapper;
    private readonly KeyMapRepository _repository;
    private readonly KeyEventQueue _queue;

    // 一般鍵 Fade 計時器 (KeyId → Timer)
    private readonly Dictionary<string, DispatcherTimer> _fadeTimers = new();

    // ── 所有鍵帽 VM（鍵盤主體資料源）────────────────────
    public ObservableCollection<KeyCapViewModel> KeyCaps { get; } = new();

    // 快速查找：KeyId → KeyCapViewModel
    private readonly Dictionary<string, KeyCapViewModel> _keyCapMap = new();

    // ── 最近按鍵（弱化，僅輔助）────────────────────────
    public ObservableCollection<RecentKeyItem> RecentKeys { get; } = new();

    // ── UI Binding 屬性 ──────────────────────────────────

    private bool _isOverlayVisible = true;
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set { _isOverlayVisible = value; OnPropertyChanged(); }
    }

    private string _currentLayoutLabel = "Standard";
    public string CurrentLayoutLabel
    {
        get => _currentLayoutLabel;
        set { _currentLayoutLabel = value; OnPropertyChanged(); }
    }

    private bool _isLocked = true;
    public bool IsLocked
    {
        get => _isLocked;
        set { _isLocked = value; OnPropertyChanged(); OnPropertyChanged(nameof(LockLabel)); }
    }

    public string LockLabel => _isLocked ? "🔒 鎖定" : "🔓 解鎖";

    private string _viewMode = "Compact";
    public string ViewMode
    {
        get => _viewMode;
        set { _viewMode = value; OnPropertyChanged(); ApplyViewMode(value); }
    }

    public bool ShowRecentKeys => _config.ShowRecentKeys;

    // ── 建構子 ───────────────────────────────────────────

    public OverlayViewModel(AppConfig config, KeyMapRepository repository)
    {
        _config = config;
        _repository = repository;
        _mapper = new KeyMapper(repository);

        _isLocked = config.OverlayLocked;
        _viewMode = config.ViewMode;
        _currentLayoutLabel = config.LayoutMode;

        // 從 KeyMapRepository 建立 KeyCapViewModel 集合
        BuildKeyCaps();

        // 套用初始 ViewMode
        ApplyViewMode(_viewMode);

        // 初始化 queue，連結 processor
        _queue = new KeyEventQueue(ProcessKeyEvent, intervalMs: 16);
    }

    // ── 建立 KeyCaps ─────────────────────────────────────

    private void BuildKeyCaps()
    {
        var entries = _repository.GetLayoutEntries();
        var isHsu = _config.LayoutMode == "Hsu";

        foreach (var entry in entries)
        {
            var vm = new KeyCapViewModel
            {
                KeyId      = entry.KeyId,
                VkCode     = entry.VkCode,
                PrimaryLabel  = entry.StandardLabel,
                SecondaryLabel = isHsu ? entry.HsuLabel : "",
                IsModifier    = entry.IsModifier,
                IsFunctionKey = entry.IsFunctionKey,
                WidthUnit     = entry.WidthMultiplier,
                Row           = entry.Row,
                Column        = entry.Col,
                LayoutGroup   = ResolveLayoutGroup(entry),
                IsVisible     = true
            };

            KeyCaps.Add(vm);
            _keyCapMap[entry.KeyId] = vm;
        }
    }

    private static string ResolveLayoutGroup(KeyMapEntry entry)
    {
        // 優先使用 JSON 中已定義的 LayoutGroup
        if (!string.IsNullOrEmpty(entry.LayoutGroup) && entry.LayoutGroup != "Main")
            return entry.LayoutGroup;

        // Fallback：依 KeyId 推斷（未來擴充 F1~F12/Nav/Numpad 時生效）
        if (entry.KeyId.StartsWith("key_f") &&
            int.TryParse(entry.KeyId[5..], out _))
            return "Function";

        if (entry.KeyId is "key_ins" or "key_del" or "key_home" or "key_end"
                        or "key_pgup" or "key_pgdn" or "key_up" or "key_down"
                        or "key_left" or "key_right" or "key_prtsc"
                        or "key_scroll" or "key_pause")
            return "Navigation";

        if (entry.KeyId.StartsWith("key_num"))
            return "Numpad";

        return "Main";
    }

    // ── Compact / Full 切換 ──────────────────────────────

    public void ApplyViewMode(string viewMode)
    {
        bool isCompact = viewMode == "Compact";
        foreach (var cap in KeyCaps)
        {
            cap.IsVisible = !isCompact || cap.LayoutGroup == "Main";
        }

        _config.ViewMode = viewMode;
        ConfigService.Save(_config);
        AppLogger.Info($"ViewMode 切換: {viewMode}");
    }

    // ── 鍵盤事件處理 ─────────────────────────────────────

    public void OnKeyEvent(KeyEventData data)
    {
        _queue.Enqueue(data);
    }

    private void ProcessKeyEvent(KeyEventData data)
    {
        var layout = _config.LayoutMode == "Hsu" ? KeyboardLayout.Hsu : KeyboardLayout.Standard;
        var displayInfo = _mapper.Map(data, layout);
        if (displayInfo == null) return;

        if (!_keyCapMap.TryGetValue(displayInfo.KeyId, out var cap)) return;

        if (cap.IsModifier)
        {
            // 修飾鍵：同步實體按住狀態，不 Fade
            cap.IsPressed = data.IsKeyDown;
        }
        else
        {
            if (!data.IsKeyDown) return; // 一般鍵只處理 KeyDown

            // 一般鍵：Pressed → 自動 Fade
            cap.FadeState = FadeState.Pressed;
            ScheduleFade(displayInfo.KeyId, cap);

            // 更新最近按鍵
            if (_config.ShowRecentKeys)
                UpdateRecentKeys(displayInfo.DisplayLabel, false);
        }

        // 同時高亮修飾鍵（KeyDown 時）
        if (data.IsKeyDown)
            HighlightModifiers(data.Modifiers);
    }

    private void ScheduleFade(string keyId, KeyCapViewModel cap)
    {
        // 重設現有計時器
        if (_fadeTimers.TryGetValue(keyId, out var existing))
        {
            existing.Stop();
            existing.Start();
            return;
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_config.FadeDurationMs)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            cap.FadeState = FadeState.Normal;
            _fadeTimers.Remove(keyId);
        };
        _fadeTimers[keyId] = timer;
        timer.Start();
    }

    private void HighlightModifiers(ModifierState mod)
    {
        SetModifierState("key_shift_l", mod.Shift);
        SetModifierState("key_shift_r", mod.Shift);
        SetModifierState("key_ctrl_l", mod.Ctrl);
        SetModifierState("key_ctrl_r", mod.Ctrl);
        SetModifierState("key_alt_l", mod.Alt);
        SetModifierState("key_alt_r", mod.Alt);
    }

    private void SetModifierState(string keyId, bool pressed)
    {
        if (_keyCapMap.TryGetValue(keyId, out var cap))
            cap.IsPressed = pressed;
    }

    private void UpdateRecentKeys(string label, bool isModifier)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        RecentKeys.Insert(0, new RecentKeyItem { Label = label, IsModifier = isModifier });
        while (RecentKeys.Count > _config.RecentKeysCount)
            RecentKeys.RemoveAt(RecentKeys.Count - 1);
    }

    // ── Layout 切換 ──────────────────────────────────────

    public void ToggleLayout()
    {
        _config.LayoutMode = _config.LayoutMode == "Hsu" ? "Standard" : "Hsu";
        CurrentLayoutLabel = _config.LayoutMode;

        bool isHsu = _config.LayoutMode == "Hsu";
        var entries = _repository.GetLayoutEntries();
        var entryMap = entries.ToDictionary(e => e.KeyId);

        foreach (var cap in KeyCaps)
        {
            if (entryMap.TryGetValue(cap.KeyId, out var entry))
                cap.SecondaryLabel = isHsu ? entry.HsuLabel : "";
        }

        ConfigService.Save(_config);
        AppLogger.Info($"切換 Layout: {_config.LayoutMode}");
    }

    // ── 其他公開方法（供 OverlayWindow 呼叫）────────────

    public AppConfig GetConfig() => _config;

    public void SaveWindowPosition(double left, double top)
    {
        if (!_config.RememberWindowPosition) return;
        _config.WindowLeft = left;
        _config.WindowTop = top;
        ConfigService.Save(_config);
    }

    public void ToggleLock()
    {
        IsLocked = !IsLocked;
        _config.OverlayLocked = IsLocked;
        ConfigService.Save(_config);
        AppLogger.Info($"Overlay 鎖定狀態: {IsLocked}");
    }

    public void ToggleViewMode()
    {
        ViewMode = ViewMode == "Compact" ? "Full" : "Compact";
    }

    // ── INotifyPropertyChanged ────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>最近按鍵顯示項目</summary>
public class RecentKeyItem
{
    public string Label { get; set; } = "";
    public bool IsModifier { get; set; }
}
