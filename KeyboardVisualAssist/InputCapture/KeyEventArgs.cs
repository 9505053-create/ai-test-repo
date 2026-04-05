namespace KeyboardVisualAssist.InputCapture;

/// <summary>
/// 標準化按鍵事件，由 KeyboardHook 發送給上層
/// 不含任何輸入內容，只含按鍵識別資訊
/// </summary>
public class KeyEventData
{
    /// <summary>Windows Virtual Key Code</summary>
    public int VirtualKey { get; init; }

    /// <summary>是否為按下（false = 放開）</summary>
    public bool IsKeyDown { get; init; }

    /// <summary>事件時間戳</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>修飾鍵狀態快照</summary>
    public ModifierState Modifiers { get; init; } = new();

    public override string ToString() =>
        $"VK=0x{VirtualKey:X2} {(IsKeyDown ? "DOWN" : "UP")} [{Modifiers}]";
}

/// <summary>修飾鍵狀態快照（在 Hook callback 瞬間擷取）</summary>
public class ModifierState
{
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Win { get; init; }
    public bool CapsLock { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        if (Shift) parts.Add("Shift");
        return string.Join("+", parts);
    }
}
