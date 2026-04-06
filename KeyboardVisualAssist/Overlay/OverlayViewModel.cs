using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using KeyboardVisualAssist.Config;
using KeyboardVisualAssist.InputCapture;
using System.Windows.Media;
using KeyboardVisualAssist.KeyMap;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Overlay;

public partial class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private readonly KeyMapper _mapper;
    private readonly KeyMapRepository _repository;
    private readonly KeyEventQueue _queue;
    private readonly Dictionary<string, DispatcherTimer> _fadeTimers = new();
    public InputStatusMonitor StatusMonitor { get; } = new();

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

    public bool ShowRecentKeys => _config.ShowRecentKeys;

    private double _overlayOpacity;
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            _overlayOpacity = Math.Clamp(value, 0.2, 1.0);
            _config.OverlayOpacity = _overlayOpacity;
            OnPropertyChanged();
        }
    }

    // ── 建構子 ───────────────────────────────────────────

    public OverlayViewModel(AppConfig config, KeyMapRepository repository)
    {
        _config = config;
        _repository = repository;
        _mapper = new KeyMapper(repository);

        _isLocked   = config.OverlayLocked;
        _viewMode   = config.ViewMode;
        _layoutMode = config.LayoutMode;
        _labelMode  = config.LabelMode;
        _scaleMode      = config.ScaleMode;
        _overlayOpacity = config.OverlayOpacity;

        BuildKeyCaps();
        ApplyViewMode(_viewMode);

        AppLogger.Info($"KeyCaps 建立完成，共 {KeyCaps.Count} 個鍵帽，ViewMode={_viewMode}");
        _queue = new KeyEventQueue(ProcessKeyEvent, intervalMs: 16);
        StatusMonitor.Start();
    }

    // ── 建立 KeyCaps ─────────────────────────────────────

    private void BuildKeyCaps()
    {
        var entries = _repository.GetLayoutEntries();
        foreach (var entry in entries)
        {
            var vm = new KeyCapViewModel
            {
                KeyId           = entry.KeyId,
                VkCode          = entry.VkCode,
                PrimaryLabel    = entry.StandardLabel,
                TraditionalLabel = entry.TraditionalLabel,
                SecondaryLabel      = entry.HsuLabel,
                SecondaryShiftLabel = entry.HsuShiftLabel,
                IsModifier      = entry.IsModifier,
                IsFunctionKey   = entry.IsFunctionKey,
                WidthUnit       = entry.WidthMultiplier,
                Row             = entry.Row,
                Column          = entry.Col,   // 現在是 double 累計偏移
                LayoutGroup     = ResolveLayoutGroup(entry),
                IsVisible       = true
            };
            KeyCaps.Add(vm);
            _keyCapMap[entry.KeyId] = vm;
        }
    }

    private static string ResolveLayoutGroup(KeyMapEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.LayoutGroup) && entry.LayoutGroup != "Main")
            return entry.LayoutGroup;
        if (entry.KeyId.StartsWith("key_f") && int.TryParse(entry.KeyId[5..], out _))
            return "Function";
        if (entry.KeyId is "key_ins" or "key_del" or "key_home" or "key_end"
                        or "key_pgup" or "key_pgdn" or "key_up" or "key_down"
                        or "key_left" or "key_right")
            return "Navigation";
        if (entry.KeyId.StartsWith("key_num"))
            return "Numpad";
        return "Main";
    }

    // ── ViewMode ─────────────────────────────────────────

    public void ApplyViewMode(string viewMode)
    {
        bool isCompact = viewMode == "Compact";
        foreach (var cap in KeyCaps)
            cap.IsVisible = !isCompact || cap.LayoutGroup == "Main";
        _config.ViewMode = viewMode;
        ConfigService.Save(_config);
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
            cap.IsPressed = data.IsKeyDown;
        }
        else
        {
            if (!data.IsKeyDown) return;

            // 清除上一個高亮（持久高亮：新鍵才清舊的）
            ClearAllNonModifierHighlights();

            cap.FadeState = FadeState.Pressed;
            // 持久高亮：不設 Fade 計時器，按新鍵才清除

            if (_config.ShowRecentKeys)
                UpdateRecentKeys(displayInfo.DisplayLabel, false);
        }

        if (data.IsKeyDown)
            HighlightModifiers(data.Modifiers);
    }

    private void ClearAllNonModifierHighlights()
    {
        foreach (var timer in _fadeTimers.Values) timer.Stop();
        _fadeTimers.Clear();
        foreach (var cap in KeyCaps)
            if (!cap.IsModifier && cap.FadeState != FadeState.Normal)
                cap.FadeState = FadeState.Normal;
    }

    /// <summary>手動清除所有高亮（清除按鈕用）</summary>
    public void ClearHighlight()
    {
        ClearAllNonModifierHighlights();
        foreach (var cap in KeyCaps)
            cap.IsPressed = false;
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
        while (RecentKeys.Count > _config.RecentKeysCount)
            RecentKeys.RemoveAt(RecentKeys.Count - 1);
    }

    // ── 切換操作 ─────────────────────────────────────────

    public void ToggleLayout()
    {
        _config.LayoutMode = _config.LayoutMode == "Hsu" ? "Standard" : "Hsu";
        LayoutMode = _config.LayoutMode;
        ConfigService.Save(_config);
        AppLogger.Info($"切換 Layout: {_config.LayoutMode} ({LayoutLabel})");
    }

    public void ToggleLock()
    {
        IsLocked = !IsLocked;
        _config.OverlayLocked = IsLocked;
        ConfigService.Save(_config);
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
        _config.LabelMode = LabelMode;
        ConfigService.Save(_config);
        AppLogger.Info($"切換 LabelMode: {LabelMode} -> Label顯示: {LabelModeLabel}");
    }

    public void IncreaseOpacity()
    {
        OverlayOpacity = Math.Min(1.0, _overlayOpacity + 0.1);
        ConfigService.Save(_config);
        AppLogger.Info($"透明度: {_overlayOpacity:F1}");
    }

    public void DecreaseOpacity()
    {
        OverlayOpacity = Math.Max(0.2, _overlayOpacity - 0.1);
        ConfigService.Save(_config);
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
        _config.ScaleMode = ScaleMode;
        ConfigService.Save(_config);
        AppLogger.Info($"切換 ScaleMode: {ScaleMode}");
    }

    // ── 輔助 ─────────────────────────────────────────────

    public AppConfig GetConfig() => _config;

    public void SaveWindowPosition(double left, double top)
    {
        if (!_config.RememberWindowPosition) return;
        _config.WindowLeft = left;
        _config.WindowTop  = top;
        ConfigService.Save(_config);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RecentKeyItem
{
    public string Label { get; set; } = "";
    public bool IsModifier { get; set; }
}
