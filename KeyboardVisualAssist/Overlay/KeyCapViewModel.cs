using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// 單一鍵帽的 ViewModel
/// 支援雙層標籤、Pressed/Fade/Modifier 狀態
/// </summary>
public class KeyCapViewModel : INotifyPropertyChanged
{
    // ── 基本識別 ─────────────────────────────────────────
    public string KeyId { get; set; } = "";
    public int VkCode { get; set; }

    // ── 雙層標籤 ─────────────────────────────────────────
    /// <summary>英文/鍵名（永遠顯示，主要大字）</summary>
    public string PrimaryLabel { get; set; } = "";

    /// <summary>許氏符號（Hsu 模式時顯示，左上小字）</summary>
    public string SecondaryLabel { get; set; } = "";

    // ── 鍵帽特性 ─────────────────────────────────────────
    /// <summary>是否為修飾鍵（Ctrl/Shift/Alt/Win/CapsLock）</summary>
    public bool IsModifier { get; set; }

    /// <summary>是否為功能鍵（F1~F12/Esc 等）</summary>
    public bool IsFunctionKey { get; set; }

    /// <summary>UI 寬度倍數（1.0 = 一般鍵寬 38px）</summary>
    public double WidthUnit { get; set; } = 1.0;

    /// <summary>UI 高度倍數</summary>
    public double HeightUnit { get; set; } = 1.0;

    // ── 版面定位 ─────────────────────────────────────────
    public int Row { get; set; }
    public int Column { get; set; }

    /// <summary>所屬功能區：Main / Function / Navigation / Numpad</summary>
    public string LayoutGroup { get; set; } = "Main";

    // ── 動態狀態（INotifyPropertyChanged） ───────────────

    private bool _isPressed;
    /// <summary>修飾鍵：pressed = 亮，released = 滅（不 Fade）</summary>
    public bool IsPressed
    {
        get => _isPressed;
        set { _isPressed = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFadeState)); }
    }

    private FadeState _fadeState = FadeState.Normal;
    /// <summary>一般鍵：Normal / Pressed / Fading</summary>
    public FadeState FadeState
    {
        get => _fadeState;
        set { _fadeState = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFadeState)); }
    }

    /// <summary>修飾鍵走 IsPressed，一般鍵走 FadeState — 統一給 Converter 用</summary>
    public FadeState DisplayFadeState => IsModifier
        ? (IsPressed ? FadeState.Pressed : FadeState.Normal)
        : _fadeState;

    private bool _isVisible = true;
    /// <summary>Compact/Full 模式控制顯示</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FadeState { Normal, Pressed, Fading }
