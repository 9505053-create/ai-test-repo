using KeyboardVisualAssist.Overlay;

namespace KeyboardVisualAssist.KeyMap;

/// <summary>
/// KeymapService 介面
/// OverlayViewModel 依賴此介面，不直接讀取 JSON 或計算座標。
/// </summary>
public interface IKeymapService
{
    /// <summary>依據目前 Repository 建立所有 KeyCapViewModel。</summary>
    IReadOnlyList<KeyCapViewModel> BuildKeyCaps();

    /// <summary>以 KeyId 為 key 的查找字典（由 BuildKeyCaps 結果建立）。</summary>
    Dictionary<string, KeyCapViewModel> BuildKeyCapMap();
}

// ── 實作 ───────────────────────────────────────────────────────────────
/// <summary>
/// 從 KeyMapRepository 解析資料，建立 UI 用的 KeyCapViewModel 集合。
///
/// 責任：
///   - 解析 keymap JSON（透過 Repository）
///   - 建立 KeyCapViewModel 列表
///   - 計算 LayoutGroup
///   - 不持有任何動態狀態（Pure Factory）
/// </summary>
public class KeymapService : IKeymapService
{
    private readonly KeyMapRepository _repository;

    public KeymapService(KeyMapRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<KeyCapViewModel> BuildKeyCaps()
    {
        var entries = _repository.GetLayoutEntries();
        var list = new List<KeyCapViewModel>(entries.Count);

        foreach (var entry in entries)
        {
            var vm = new KeyCapViewModel
            {
                KeyId               = entry.KeyId,
                VkCode              = entry.VkCode,
                PrimaryLabel        = entry.StandardLabel,
                TraditionalLabel    = entry.TraditionalLabel,
                SecondaryLabel      = entry.HsuLabel,
                SecondaryShiftLabel = entry.HsuShiftLabel,
                IsModifier          = entry.IsModifier,
                IsFunctionKey       = entry.IsFunctionKey,
                WidthUnit           = entry.WidthMultiplier,
                Row                 = entry.Row,
                Column              = entry.Col,
                LayoutGroup         = ResolveLayoutGroup(entry),
                IsVisible           = true
            };
            list.Add(vm);
        }

        return list.AsReadOnly();
    }

    public Dictionary<string, KeyCapViewModel> BuildKeyCapMap()
    {
        var caps = BuildKeyCaps();
        var map = new Dictionary<string, KeyCapViewModel>(caps.Count);
        foreach (var cap in caps)
            map[cap.KeyId] = cap;
        return map;
    }

    // ── 私有輔助 ──────────────────────────────────────────────────────

    private static string ResolveLayoutGroup(KeyMapEntry entry)
    {
        // 優先使用 JSON 中明確指定的群組（非 Main）
        if (!string.IsNullOrEmpty(entry.LayoutGroup) && entry.LayoutGroup != "Main")
            return entry.LayoutGroup;

        // Function Row（F1–F12）
        if (entry.KeyId.StartsWith("key_f") && int.TryParse(entry.KeyId[5..], out _))
            return "Function";

        // Navigation cluster
        if (entry.KeyId is "key_ins" or "key_del" or "key_home" or "key_end"
                        or "key_pgup" or "key_pgdn" or "key_up" or "key_down"
                        or "key_left" or "key_right")
            return "Navigation";

        // Numpad
        if (entry.KeyId.StartsWith("key_num"))
            return "Numpad";

        return "Main";
    }
}
