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
        _colorTheme     = Cfg.ColorTheme;
        _bgTheme        = Cfg.BgTheme;
        _showGuideLines = Cfg.ShowGuideLines;

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
        {
            // Compact：只顯示主鍵區 + Function列（ESC/F1~F12 永遠顯示）
            // Full：顯示全部
            cap.IsVisible = !isCompact
                || cap.LayoutGroup == "Main"
                || cap.LayoutGroup == "Function";
        }
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

        // 修飾鍵組合高亮同步：KeyDown 和 KeyUp 都要更新
        // 避免「Shift 按下但 Modifiers 快照未即時反映」問題：
        // 若按下的就是修飾鍵本身，以 IsKeyDown 直接覆寫快照值
        var mod = data.Modifiers;
        int vk = data.VirtualKey;
        SyncModifierHighlights(mod, vk, data.IsKeyDown);
    }

    private void SyncModifierHighlights(ModifierState mod, int vkCode, bool isKeyDown)
    {
        // 修飾鍵 VK 碼
        const int VK_SHIFT_L   = 0xA0;
        const int VK_SHIFT_R   = 0xA1;
        const int VK_CTRL_L    = 0xA2;
        const int VK_CTRL_R    = 0xA3;
        const int VK_ALT_L     = 0xA4;
        const int VK_ALT_R     = 0xA5;
        const int VK_SHIFT     = 0x10;
        const int VK_CTRL      = 0x11;
        const int VK_ALT       = 0x12;

        // 若按下/放開的就是修飾鍵本身，以 isKeyDown 修正快照可能的誤差
        bool shiftDown = mod.Shift;
        bool ctrlDown  = mod.Ctrl;
        bool altDown   = mod.Alt;

        if (vkCode is VK_SHIFT or VK_SHIFT_L or VK_SHIFT_R) shiftDown = isKeyDown;
        if (vkCode is VK_CTRL  or VK_CTRL_L  or VK_CTRL_R)  ctrlDown  = isKeyDown;
        if (vkCode is VK_ALT   or VK_ALT_L   or VK_ALT_R)   altDown   = isKeyDown;

        SetModifierState("key_shift_l", shiftDown);
        SetModifierState("key_shift_r", shiftDown);
        SetModifierState("key_ctrl_l",  ctrlDown);
        SetModifierState("key_ctrl_r",  ctrlDown);
        SetModifierState("key_alt_l",   altDown);
        SetModifierState("key_alt_r",   altDown);
    }

    private void SetModifierState(string keyId, bool pressed)
    {
        if (_keyCapMap.TryGetValue(keyId, out var cap)) cap.IsPressed = pressed;
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

    private void UpdateRecentKeys(string label, bool isModifier)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        RecentKeys.Insert(0, new RecentKeyItem { Label = label, IsModifier = isModifier });
        while (RecentKeys.Count > Cfg.RecentKeysCount)
            RecentKeys.RemoveAt(RecentKeys.Count - 1);
    }

    // ── 切換操作 ─────────────────────────────────────────

    // ── 剪貼簿顯示 ───────────────────────────────────────
    private bool _showClipboard = false;
    public bool ShowClipboard
    {
        get => _showClipboard;
        set { _showClipboard = value; OnPropertyChanged(); OnPropertyChanged(nameof(ClipboardLabel)); if (value) RefreshClipboard(); }
    }
    public string ClipboardLabel => _showClipboard ? "剪貼簿" : "剪貼簿";

    private string _clipboardText = "";
    public string ClipboardText
    {
        get => _clipboardText;
        set { _clipboardText = value; OnPropertyChanged(); }
    }

    public void ToggleClipboard()
    {
        ShowClipboard = !ShowClipboard;
        AppLogger.Info($"剪貼簿顯示: {ShowClipboard}");
    }

    public void RefreshClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
                ClipboardText = System.Windows.Clipboard.GetText();
            else
                ClipboardText = "（剪貼簿無文字內容）";
        }
        catch { ClipboardText = "（無法讀取剪貼簿）"; }
    }

    // ── 輔助線 ───────────────────────────────────────────
    private bool _showGuideLines = true;
    public bool ShowGuideLines
    {
        get => _showGuideLines;
        set { _showGuideLines = value; OnPropertyChanged(); OnPropertyChanged(nameof(GuideLinesLabel)); Cfg.ShowGuideLines = value; _configSvc.SaveDebounced(); }
    }
    public string GuideLinesLabel => _showGuideLines ? "輔助線" : "無線";

    public void ToggleGuideLines()
    {
        ShowGuideLines = !ShowGuideLines;
        AppLogger.Info($"輔助線: {ShowGuideLines}");
    }

    // ── 鍵盤底色主題 ─────────────────────────────────────
    // Dark（預設黑）/ Darker（更黑）/ Gray（深灰）/ Blue（深藍）/ Green（深綠）

    private string _bgTheme = "Dark";
    public string BgTheme
    {
        get => _bgTheme;
        set { _bgTheme = value; OnPropertyChanged(); OnPropertyChanged(nameof(BgThemeLabel)); OnPropertyChanged(nameof(KeyboardBackground)); }
    }
    public string BgThemeLabel => _bgTheme switch
    {
        "Darker" => "黑²",
        "Gray"   => "灰底",
        "Blue"   => "藍底",
        "Green"  => "綠底",
        _        => "黑底"
    };
    public System.Windows.Media.SolidColorBrush KeyboardBackground => _bgTheme switch
    {
        "Darker" => new(System.Windows.Media.Color.FromRgb(0x08, 0x08, 0x08)),
        "Gray"   => new(System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x30)),
        "Blue"   => new(System.Windows.Media.Color.FromRgb(0x08, 0x10, 0x28)),
        "Green"  => new(System.Windows.Media.Color.FromRgb(0x08, 0x20, 0x10)),
        _        => new(System.Windows.Media.Color.FromRgb(0x16, 0x16, 0x16)),
    };

    public void CycleBgTheme()
    {
        BgTheme = BgTheme switch
        {
            "Dark"   => "Darker",
            "Darker" => "Gray",
            "Gray"   => "Blue",
            "Blue"   => "Green",
            _        => "Dark"
        };
        Cfg.BgTheme = BgTheme;
        _configSvc.SaveDebounced();
        AppLogger.Info($"切換底色: {BgTheme}");
    }
    // 主題定義：英文/許氏/注音 三層顏色
    // Default: 英=白 許=紅 注=藍
    // Warm:    英=白 許=橙 注=黃
    // Cool:    英=青 許=紫 注=綠
    // Mono:    英=白 許=灰 注=灰

    private string _colorTheme = "Default";
    public string ColorTheme
    {
        get => _colorTheme;
        set { _colorTheme = value; OnPropertyChanged(); OnPropertyChanged(nameof(ColorThemeLabel)); }
    }
    public string ColorThemeLabel => _colorTheme switch
    {
        "Warm" => "暖",
        "Cool" => "冷",
        "Mono" => "灰",
        _      => "色"
    };

    public void CycleColorTheme()
    {
        ColorTheme = ColorTheme switch
        {
            "Default" => "Warm",
            "Warm"    => "Cool",
            "Cool"    => "Mono",
            _         => "Default"
        };
        Cfg.ColorTheme = ColorTheme;
        _configSvc.SaveDebounced();
        AppLogger.Info($"切換顏色主題: {ColorTheme}");
    }

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
