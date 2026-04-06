namespace KeyboardVisualAssist.KeyMap;

/// <summary>
/// 單鍵完整資料模型 v1.1
/// </summary>
public class KeyMapEntry
{
    public string KeyId { get; set; } = "";
    public string VirtualKey { get; set; } = "";
    public int VkCode { get; set; }

    public string StandardLabel { get; set; } = "";
    public string StandardShiftLabel { get; set; } = "";
    public string HsuLabel { get; set; } = "";
    public string HsuShiftLabel { get; set; } = "";

    public bool IsModifier { get; set; }
    public bool IsFunctionKey { get; set; }
    public bool IsWideKey { get; set; }

    public int Row { get; set; }
    public double Col { get; set; }
    public double WidthMultiplier { get; set; } = 1.0;

    /// <summary>功能區分類：Main / Function / Navigation / Numpad</summary>
    public string LayoutGroup { get; set; } = "Main";
}

public enum KeyboardLayout { Standard, Hsu }

public class KeyDisplayInfo
{
    public string KeyId { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string HsuLabel { get; set; } = "";
    public bool IsModifier { get; set; }
    public bool IsFunctionKey { get; set; }
}
