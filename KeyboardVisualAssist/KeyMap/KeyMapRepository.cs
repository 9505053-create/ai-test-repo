using System.IO;
using System.Text.Json;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.KeyMap;

/// <summary>
/// 載入並管理所有 keymap JSON
/// 提供 VK code 查詢接口
/// </summary>
public class KeyMapRepository
{
    private readonly Dictionary<int, KeyMapEntry> _standardMap = new();
    private readonly Dictionary<int, KeyMapEntry> _hsuMap = new();
    private List<KeyMapEntry> _allEntries = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        // 允許 JSON 內有 // 注釋（JSONC 風格），雙重保險
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<KeyMapEntry> AllEntries => _allEntries.AsReadOnly();

    public void LoadAll()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        LoadLayout(Path.Combine(baseDir, "assets", "keymap.standard.json"), _standardMap);
        LoadLayout(Path.Combine(baseDir, "assets", "keymap.hsu.json"), _hsuMap);

        // AllEntries 以 standard 為基礎，合併 hsu 的注音資料
        foreach (var entry in _standardMap.Values)
        {
            if (_hsuMap.TryGetValue(entry.VkCode, out var hsuEntry))
            {
                entry.HsuLabel = hsuEntry.HsuLabel;
                entry.HsuShiftLabel = hsuEntry.HsuShiftLabel;
            }
        }
        _allEntries = _standardMap.Values.ToList();

        AppLogger.Info($"KeyMap 載入完成：{_allEntries.Count} 個按鍵");
    }

    private void LoadLayout(string path, Dictionary<int, KeyMapEntry> target)
    {
        if (!File.Exists(path))
        {
            AppLogger.Error($"找不到 keymap 檔案：{path}");
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<KeyMapEntry>>(json, JsonOpts);
            if (entries == null) return;

            foreach (var entry in entries)
            {
                // 解析 VK hex 字串
                if (entry.VirtualKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    entry.VkCode = Convert.ToInt32(entry.VirtualKey, 16);
                else if (int.TryParse(entry.VirtualKey, out var vk))
                    entry.VkCode = vk;

                if (entry.VkCode > 0)
                    target[entry.VkCode] = entry;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"載入 keymap 失敗：{path}", ex);
        }
    }

    /// <summary>依 VK code 查詢按鍵資料</summary>
    public KeyMapEntry? GetByVk(int vkCode)
        => _standardMap.TryGetValue(vkCode, out var entry) ? entry : null;

    /// <summary>取得所有按鍵的排列版面（用於繪製鍵盤圖）</summary>
    public List<KeyMapEntry> GetLayoutEntries()
        => _allEntries.OrderBy(e => e.Row).ThenBy(e => e.Col).ToList();
}
