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
    /// <summary>許氏符號（紅色）</summary>
    public string SecondaryLabel { get; set; } = "";

    // ── 特性 ─────────────────────────────────────────────
    public bool IsModifier { get; set; }
    public bool IsFunctionKey { get; set; }
    public double WidthUnit { get; set; } = 1.0;
    public double HeightUnit { get; set; } = 1.0;
    public int Row { get; set; }
    public double Column { get; set; }   // 改為 double，支援累計偏移
    public string LayoutGroup { get; set; } = "Main";

    // ── 動態狀態 ─────────────────────────────────────────
    private bool _isPressed;
    public bool IsPressed
    {
        get => _isPressed;
        set { _isPressed = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFadeState)); }
    }

    private FadeState _fadeState = FadeState.Normal;
    public FadeState FadeState
    {
        get => _fadeState;
        set { _fadeState = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFadeState)); }
    }

    public FadeState DisplayFadeState => IsModifier
        ? (IsPressed ? FadeState.Pressed : FadeState.Normal)
        : _fadeState;

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

public enum FadeState { Normal, Pressed, Fading }
