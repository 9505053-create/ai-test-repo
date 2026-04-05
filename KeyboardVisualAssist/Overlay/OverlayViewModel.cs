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
/// Overlay 視窗的 ViewModel
/// 管理高亮狀態、最近按鍵序列、淡出計時
/// </summary>
public partial class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private readonly KeyMapper _mapper;
    private readonly KeyMapRepository _repository;
    private readonly KeyEventQueue _queue;
    private readonly Dictionary<string, DispatcherTimer> _fadeTimers = new();

    // UI Binding 屬性
    private bool _isOverlayVisible = true;
    private string _currentLayoutLabel = "Standard";

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set { _isOverlayVisible = value; OnPropertyChanged(); }
    }

    public string CurrentLayoutLabel
    {
        get => _currentLayoutLabel;
        set { _currentLayoutLabel = value; OnPropertyChanged(); }
    }

    /// <summary>最近按下的鍵（最多 N 個，新的在前）</summary>
    public ObservableCollection<RecentKeyItem> RecentKeys { get; } = new();

    /// <summary>目前高亮的 KeyId 集合（支援多鍵同時高亮）</summary>
    public ObservableCollection<string> HighlightedKeyIds { get; } = new();

    /// <summary>所有鍵盤按鍵的版面資料（用於繪製鍵盤圖）</summary>
    public List<KeyMapEntry> LayoutEntries { get; }

    public OverlayViewModel(AppConfig config, KeyMapRepository repository)
    {
        _config = config;
        _repository = repository;
        _mapper = new KeyMapper(repository);
        LayoutEntries = repository.GetLayoutEntries();

        CurrentLayoutLabel = config.ActiveLayout;

        // 初始化 queue，連結 processor
        _queue = new KeyEventQueue(ProcessKeyEvent, intervalMs: 16);
    }

    /// <summary>由 KeyboardHook 觸發，立即進 queue</summary>
    public void OnKeyEvent(KeyEventData data)
    {
        _queue.Enqueue(data);
    }

    /// <summary>由 queue timer 在 UI thread 呼叫</summary>
    private void ProcessKeyEvent(KeyEventData data)
    {
        if (!data.IsKeyDown) return; // MVP 只處理 KeyDown

        var layout = _config.ActiveLayout == "Hsu" ? KeyboardLayout.Hsu : KeyboardLayout.Standard;
        var displayInfo = _mapper.Map(data, layout);
        if (displayInfo == null) return;

        // 更新最近按鍵
        UpdateRecentKeys(displayInfo.DisplayLabel, displayInfo.IsModifier);

        // 更新高亮
        SetHighlight(displayInfo.KeyId);

        // 同時高亮修飾鍵
        HighlightModifiers(data.Modifiers);
    }

    private void UpdateRecentKeys(string label, bool isModifier)
    {
        if (string.IsNullOrWhiteSpace(label)) return;

        var item = new RecentKeyItem { Label = label, IsModifier = isModifier };
        RecentKeys.Insert(0, item);

        while (RecentKeys.Count > _config.RecentKeyCount)
            RecentKeys.RemoveAt(RecentKeys.Count - 1);
    }

    private void SetHighlight(string keyId)
    {
        if (string.IsNullOrEmpty(keyId)) return;

        if (!HighlightedKeyIds.Contains(keyId))
            HighlightedKeyIds.Add(keyId);

        // 重設淡出計時
        if (_fadeTimers.TryGetValue(keyId, out var existing))
        {
            existing.Stop();
            existing.Start();
            return;
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_config.HighlightDurationMs)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            HighlightedKeyIds.Remove(keyId);
            _fadeTimers.Remove(keyId);
        };
        _fadeTimers[keyId] = timer;
        timer.Start();
    }

    private void HighlightModifiers(ModifierState mod)
    {
        if (mod.Shift) SetHighlight("key_shift_l");
        if (mod.Ctrl) SetHighlight("key_ctrl_l");
        if (mod.Alt) SetHighlight("key_alt_l");
    }

    public void ToggleLayout()
    {
        _config.ActiveLayout = _config.ActiveLayout == "Hsu" ? "Standard" : "Hsu";
        CurrentLayoutLabel = _config.ActiveLayout;
        ConfigService.Save(_config);
        AppLogger.Info($"切換 Layout: {_config.ActiveLayout}");
    }

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
