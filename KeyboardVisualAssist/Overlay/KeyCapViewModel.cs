using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KeyboardVisualAssist.Overlay;

public class KeyCapViewModel : INotifyPropertyChanged
{
    // ── 識別 ──────────────────────────────────────────────
    public string KeyId { get; set; } = "";
    public int VkCode { get; set; }

    // ── 三層標籤 ─────────────────────────────────────────
    /// <summary>英文/鍵名（白色，永遠存在）</summary>
    public string PrimaryLabel { get; set; } = "";
    /// <summary>傳統注音（藍色，Standard 模式下的注音）</summary>
    public string TraditionalLabel { get; set; } = "";
    /// <summary>許氏主音（紅色，左下角）</summary>
    public string SecondaryLabel { get; set; } = "";
    /// <summary>許氏 Shift 音（橙色，左上角）</summary>
    public string SecondaryShiftLabel { get; set; } = "";

    // ── 特性 ─────────────────────────────────────────────
    public bool IsModifier { get; set; }
    public bool IsFunctionKey { get; set; }
    public double WidthUnit { get; set; } = 1.0;
    public double HeightUnit { get; set; } = 1.0;
    public int Row { get; set; }
    public double Column { get; set; }
    public string LayoutGroup { get; set; } = "Main";

    // ── 動態狀態 ─────────────────────────────────────────
    private bool _isPressed;
    public bool IsPressed
    {
        get => _isPressed;
        set { _isPressed = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayState)); }
    }

    private KeyCapState _state = KeyCapState.Normal;
    public KeyCapState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayState)); }
    }

    /// <summary>
    /// 修飾鍵：由 IsPressed 決定顯示狀態（Pressed / Normal）。
    /// 一般鍵：直接反映 State（Normal / Pressed / Fading）。
    /// </summary>
    public KeyCapState DisplayState => IsModifier
        ? (IsPressed ? KeyCapState.Pressed : KeyCapState.Normal)
        : _state;

    // ── 相容屬性（供 XAML DataTrigger 過渡期使用）────────
    /// <summary>舊名稱橋接，等 XAML 全改完後可移除</summary>
    public KeyCapState FadeState
    {
        get => _state;
        set => State = value;
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>鍵帽視覺狀態機狀態</summary>
public enum KeyCapState
{
    /// <summary>預設，無高亮</summary>
    Normal,
    /// <summary>按下中（一般鍵按下瞬間 / 修飾鍵按住中）</summary>
    Pressed,
    /// <summary>僅一般鍵：放開後短暫殘影，計時結束後回到 Normal</summary>
    Fading,
    /// <summary>預留：CapsLock 鎖定態 / 未來切換鍵用</summary>
    Latched
}


