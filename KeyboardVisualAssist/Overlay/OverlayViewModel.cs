using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using KeyboardVisualAssist.Config;
using KeyboardVisualAssist.InputCapture;
using KeyboardVisualAssist.KeyMap;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Overlay;

public partial class OverlayViewModel : INotifyPropertyChanged
{
    private readonly IConfigService _configSvc;
    private readonly IKeymapService _keymapSvc;
    private readonly KeyMapper _mapper;
    private readonly KeyEventQueue _queue;
    private readonly KeyCapStateMachine _stateMachine;
    public InputStatusMonitor StatusMonitor { get; } = new();

    // 方便存取
    private AppConfig Cfg => _configSvc.Config;

    public ObservableCollection<KeyCapViewModel> KeyCaps { get; } = new();
    private readonly Dictionary<string, KeyCapViewModel> _keyCapMap = new();
    public ObservableCollection<RecentKeyItem> RecentKeys { get; } = new();

    // ── UI 屬性 ──────────────────────────────────────────

    private bool _isOverlayVisible = true;
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set { _isOverlayVisible = value; OnPropertyChanged(); }
    }

    private bool _isLocked = false;
    public bool IsLocked
    {
        get => _isLocked;
        set { _isLocked = value; OnPropertyChanged(); OnPropertyChanged(nameof(LockLabel)); }
    }
    public string LockLabel => _isLocked ? "🔒" : "🔓";

    private string _layoutMode = "Standard";
    public string LayoutMode
    {
        get => _layoutMode;
        set { _layoutMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(LayoutLabel)); }
    }
    /// <summary>顯示用短標籤</summary>
    public string LayoutLabel => _layoutMode == "Hsu" ? "許氏" : "標準";

    private string _viewMode = "Compact";
    public string ViewMode
    {
        get => _viewMode;
        set { _viewMode = value; OnPropertyChanged(); }
    }

    private string _labelMode = "All";
    public string LabelMode
    {
        get => _labelMode;
        set { _labelMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(LabelModeLabel)); }
    }
    public string LabelModeLabel => _labelMode switch
    {
        "EnglishOnly"     => "英",
        "TraditionalOnly" => "注",
        "HsuOnly"         => "許",
        "EnglishAndHsu"   => "英+許",
        _                 => "全",
    };

    // 視窗縮放比例（Small=0.75, Medium=1.0, Large=1.35）
    private string _scaleMode = "Small";
    public string ScaleMode
    {
        get => _scaleMode;
        set { _scaleMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScaleLabel)); OnPropertyChanged(nameof(WindowScale)); }
    }
    public string ScaleLabel => _scaleMode switch { "Large" => "大", "Medium" => "中", _ => "小" };
    public double WindowScale => _scaleMode switch { "Large" => 1.35, "Medium" => 1.0, _ => 0.75 };

    public bool ShowRecentKeys => Cfg.ShowRecentKeys;

    private double _overlayOpacity;
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            _overlayOpacity = Math.Clamp(value, 0.2, 1.0);
            Cfg.OverlayOpacity = _overlayOpacity;
            OnPropertyChanged();
        }
    }

    // ── 建構子 ───────────────────────────────────────────

    public OverlayViewModel(IConfigService configSvc, IKeymapService keymapSvc, KeyMapRepository repository)
    {
        _configSvc  = configSvc;
        _keymapSvc  = keymapSvc;
        _mapper     = new KeyMapper(repository);

        _isLocked       = Cfg.OverlayLocked;
        _viewMode       = Cfg.ViewMode;
        _layoutMode     = Cfg.LayoutMode;
        _labelMode      = Cfg.LabelMode;
        _scaleMode      = Cfg.ScaleMode;
        _overlayOpacity = Cfg.OverlayOpacity;

        BuildKeyCaps();
        ApplyViewMode(_viewMode);

        AppLogger.Info($"KeyCaps 建立完成，共 {KeyCaps.Count} 個鍵帽，ViewMode={_viewMode}");
        _stateMachine = new KeyCapStateMachine(Cfg.FadeDurationMs);
        _queue = new KeyEventQueue(ProcessKeyEvent, intervalMs: 16);
        StatusMonitor.Start();
    }

    // ── 建立 KeyCaps（委派給 KeymapService）────────────────

    private void BuildKeyCaps()
    {
        var caps = _keymapSvc.BuildKeyCaps();
        foreach (var cap in caps)
        {
            KeyCaps.Add(cap);
            _keyCapMap[cap.KeyId] = cap;
        }
    }

    // ── ViewMode ─────────────────────────────────────────

    public void ApplyViewMode(string viewMode)
    {
        bool isCompact = viewMode == "Compact";
        foreach (var cap in KeyCaps)
            cap.IsVisible = !isCompact || cap.LayoutGroup == "Main";
        Cfg.ViewMode = viewMode;
        _configSvc.SaveDebounced();
    }

    // ── 鍵盤事件 ─────────────────────────────────────────

    public void OnKeyEvent(KeyEventData data) => _queue.Enqueue(data);

    private void ProcessKeyEvent(KeyEventData data)
    {
        var layout = _layoutMode == "Hsu" ? KeyboardLayout.Hsu : KeyboardLayout.Standard;
        var displayInfo = _mapper.Map(data, layout);
        if (displayInfo == null) return;
        if (!_keyCapMap.TryGetValue(displayInfo.KeyId, out var cap)) return;

        if (cap.IsModifier)
        {
            // 修飾鍵：直接同步實體狀態，無 Fading
            _stateMachine.OnModifierChanged(cap, data.IsKeyDown);
        }
        else
        {
            if (data.IsKeyDown)
            {
                _stateMachine.OnNormalKeyDown(cap);

                if (Cfg.ShowRecentKeys)
                    UpdateRecentKeys(displayInfo.DisplayLabel, false);
            }
            else
            {
                _stateMachine.OnNormalKeyUp(cap);
            }
        }

        // 修飾鍵組合高亮同步（按下時更新）
        if (data.IsKeyDown)
            HighlightModifiers(data.Modifiers);
    }

    private void ClearAllNonModifierHighlights()
    {
        _stateMachine.ClearAllNormalKeys(KeyCaps);
    }

    /// <summary>手動清除所有高亮（清除按鈕用）</summary>
    public void ClearHighlight()
    {
        _stateMachine.ClearAll(KeyCaps);
        RecentKeys.Clear();
        AppLogger.Info("手動清除高亮");
    }

    private void HighlightModifiers(ModifierState mod)
    {
        SetModifierState("key_shift_l", mod.Shift);
        SetModifierState("key_shift_r", mod.Shift);
        SetModifierState("key_ctrl_l",  mod.Ctrl);
        SetModifierState("key_ctrl_r",  mod.Ctrl);
        SetModifierState("key_alt_l",   mod.Alt);
        SetModifierState("key_alt_r",   mod.Alt);
    }

    private void SetModifierState(string keyId, bool pressed)
    {
        if (_keyCapMap.TryGetValue(keyId, out var cap)) cap.IsPressed = pressed;
    }

    private void UpdateRecentKeys(string label, bool isModifier)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        RecentKeys.Insert(0, new RecentKeyItem { Label = label, IsModifier = isModifier });
        while (RecentKeys.Count > Cfg.RecentKeysCount)
            RecentKeys.RemoveAt(RecentKeys.Count - 1);
    }

    // ── 切換操作 ─────────────────────────────────────────

    public void ToggleLayout()
    {
        Cfg.LayoutMode = Cfg.LayoutMode == "Hsu" ? "Standard" : "Hsu";
        LayoutMode = Cfg.LayoutMode;
        _configSvc.SaveDebounced();
        AppLogger.Info($"切換 Layout: {Cfg.LayoutMode} ({LayoutLabel})");
    }

    public void ToggleLock()
    {
        IsLocked = !IsLocked;
        Cfg.OverlayLocked = IsLocked;
        _configSvc.SaveDebounced();
    }

    public void ToggleViewMode()
    {
        var next = ViewMode == "Compact" ? "Full" : "Compact";
        ViewMode = next;
        ApplyViewMode(next);
    }

    public void CycleLabelMode()
    {
        LabelMode = LabelMode switch
        {
            "All"             => "EnglishOnly",
            "EnglishOnly"     => "TraditionalOnly",
            "TraditionalOnly" => "HsuOnly",
            "HsuOnly"         => "EnglishAndHsu",
            _                 => "All"
        };
        Cfg.LabelMode = LabelMode;
        _configSvc.SaveDebounced();
        AppLogger.Info($"切換 LabelMode: {LabelMode} -> {LabelModeLabel}");
    }

    public void IncreaseOpacity()
    {
        OverlayOpacity = Math.Min(1.0, _overlayOpacity + 0.1);
        _configSvc.SaveDebounced();
        AppLogger.Info($"透明度: {_overlayOpacity:F1}");
    }

    public void DecreaseOpacity()
    {
        OverlayOpacity = Math.Max(0.2, _overlayOpacity - 0.1);
        _configSvc.SaveDebounced();
        AppLogger.Info($"透明度: {_overlayOpacity:F1}");
    }

    public void CycleScaleMode()
    {
        ScaleMode = ScaleMode switch
        {
            "Small"  => "Medium",
            "Medium" => "Large",
            _        => "Small"
        };
        Cfg.ScaleMode = ScaleMode;
        _configSvc.SaveDebounced();
        AppLogger.Info($"切換 ScaleMode: {ScaleMode}");
    }

    // ── 輔助 ─────────────────────────────────────────────

    public AppConfig GetConfig() => Cfg;

    public void SaveWindowPosition(double left, double top)
        => _configSvc.SaveWindowPosition(left, top);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RecentKeyItem
{
    public string Label { get; set; } = "";
    public bool IsModifier { get; set; }
}
