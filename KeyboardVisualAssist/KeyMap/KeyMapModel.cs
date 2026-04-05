namespace KeyboardVisualAssist.KeyMap;

/// <summary>
/// 單鍵完整資料模型
/// 由 JSON 定義，與 UI 解耦
/// </summary>
public class KeyMapEntry
{
    /// <summary>內部 ID，對應 UI 鍵盤圖的 key element</summary>
    public string KeyId { get; set; } = "";

    /// <summary>Windows Virtual Key Code (十六進位字串，如 "0x41")</summary>
    public string VirtualKey { get; set; } = "";

    /// <summary>實際 VK int 值（由 Repository 解析後填入）</summary>
    public int VkCode { get; set; }

    /// <summary>Standard 排列下的顯示名稱</summary>
    public string StandardLabel { get; set; } = "";

    /// <summary>Standard Shift 狀態下的顯示名稱</summary>
    public string StandardShiftLabel { get; set; } = "";

    /// <summary>許氏排列下的顯示名稱（注音/符號）</summary>
    public string HsuLabel { get; set; } = "";

    /// <summary>許氏 Shift 狀態下的顯示名稱</summary>
    public string HsuShiftLabel { get; set; } = "";

    /// <summary>是否為修飾鍵（Ctrl/Shift/Alt/Win/CapsLock）</summary>
    public bool IsModifier { get; set; }

    /// <summary>是否為功能鍵（F1-F12/Esc/Del 等）</summary>
    public bool IsFunctionKey { get; set; }

    /// <summary>是否為寬鍵（Tab/Enter/Shift 等，影響 UI 渲染寬度）</summary>
    public bool IsWideKey { get; set; }

    /// <summary>鍵盤版面列（0=數字列, 1=QWERTY, 2=ASDF, 3=ZXCV, 4=底部）</summary>
    public int Row { get; set; }

    /// <summary>鍵盤版面欄（0-based）</summary>
    public int Col { get; set; }

    /// <summary>UI 寬度倍數（1.0 = 一般鍵寬）</summary>
    public double WidthMultiplier { get; set; } = 1.0;
}

/// <summary>Layout 類型</summary>
public enum KeyboardLayout
{
    Standard,
    Hsu
}

/// <summary>
/// 按下按鍵後，給 UI 用的顯示資料
/// </summary>
public class KeyDisplayInfo
{
    public string KeyId { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string HsuLabel { get; set; } = "";
    public bool IsModifier { get; set; }
    public bool IsFunctionKey { get; set; }
}
