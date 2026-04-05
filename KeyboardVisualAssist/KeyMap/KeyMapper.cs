using KeyboardVisualAssist.InputCapture;

namespace KeyboardVisualAssist.KeyMap;

/// <summary>
/// 將原始 VK code 轉為 UI 可用的顯示資料
/// 支援 Standard / Hsu 兩種 Layout
/// </summary>
public class KeyMapper
{
    private readonly KeyMapRepository _repository;

    public KeyMapper(KeyMapRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// 將按鍵事件轉為顯示資訊
    /// </summary>
    public KeyDisplayInfo? Map(KeyEventData keyEvent, KeyboardLayout layout)
    {
        var entry = _repository.GetByVk(keyEvent.VirtualKey);
        if (entry == null) return null;

        bool isShift = keyEvent.Modifiers.Shift || keyEvent.Modifiers.CapsLock;

        string displayLabel = layout switch
        {
            KeyboardLayout.Hsu => isShift
                ? (string.IsNullOrEmpty(entry.HsuShiftLabel) ? entry.HsuLabel : entry.HsuShiftLabel)
                : entry.HsuLabel,
            _ => isShift
                ? (string.IsNullOrEmpty(entry.StandardShiftLabel) ? entry.StandardLabel : entry.StandardShiftLabel)
                : entry.StandardLabel
        };

        if (string.IsNullOrEmpty(displayLabel))
            displayLabel = entry.StandardLabel;

        // 加上修飾鍵前綴
        var prefix = BuildModifierPrefix(keyEvent.Modifiers, entry.IsModifier);
        if (!string.IsNullOrEmpty(prefix))
            displayLabel = prefix + displayLabel;

        return new KeyDisplayInfo
        {
            KeyId = entry.KeyId,
            DisplayLabel = displayLabel,
            HsuLabel = entry.HsuLabel,
            IsModifier = entry.IsModifier,
            IsFunctionKey = entry.IsFunctionKey
        };
    }

    private static string BuildModifierPrefix(ModifierState mod, bool isModifierKey)
    {
        if (isModifierKey) return ""; // 修飾鍵本身不加前綴

        var parts = new List<string>();
        if (mod.Ctrl) parts.Add("Ctrl");
        if (mod.Alt) parts.Add("Alt");
        if (mod.Win) parts.Add("Win");
        // Shift 不加前綴（反映在 label 選擇）

        return parts.Count > 0 ? string.Join("+", parts) + "+" : "";
    }
}
