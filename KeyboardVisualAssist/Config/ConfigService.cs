using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Config;

/// <summary>應用程式設定模型 v1.1</summary>
public class AppConfig
{
    // ── 顯示模式 ──────────────────────────────────────────
    /// <summary>AlwaysVisible = 常駐顯示；TargetAppsOnly = 指定程式才顯示</summary>
    public string DisplayMode { get; set; } = "AlwaysVisible";

    /// <summary>Compact = 僅主鍵區；Full = 完整鍵盤</summary>
    public string ViewMode { get; set; } = "Compact";

    /// <summary>Standard / Hsu</summary>
    public string LayoutMode { get; set; } = "Standard";

    // ── 視窗位置與狀態 ────────────────────────────────────
    /// <summary>true = click-through 鎖定；false = 可拖曳</summary>
    public bool OverlayLocked { get; set; } = false;

    /// <summary>是否記憶視窗位置</summary>
    public bool RememberWindowPosition { get; set; } = true;

    /// <summary>視窗左上角 X（-1 = 自動右下角）</summary>
    public double WindowLeft { get; set; } = -1;

    /// <summary>視窗左上角 Y（-1 = 自動右下角）</summary>
    public double WindowTop { get; set; } = -1;

    // ── 外觀 ──────────────────────────────────────────────
    public double OverlayOpacity { get; set; } = 0.88;
    public int FontSize { get; set; } = 12;

    // ── 高亮 ──────────────────────────────────────────────
    /// <summary>一般鍵 Fade 持續時間（ms）</summary>
    public int FadeDurationMs { get; set; } = 350;

    // ── 最近按鍵 ──────────────────────────────────────────
    public bool ShowRecentKeys { get; set; } = true;
    public int RecentKeysCount { get; set; } = 3;

    // ── TargetAppsOnly 模式用 ────────────────────────────
    public List<string> TargetApps { get; set; } = new()
    {
        "WINWORD.EXE", "EXCEL.EXE", "OUTLOOK.EXE",
        "notepad.exe", "notepad++.exe", "POWERPNT.EXE"
    };
}

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static string ConfigPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("讀取 config.json 失敗，使用預設值", ex);
        }

        var def = new AppConfig();
        Save(def);
        return def;
    }

    public static void Save(AppConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("儲存 config.json 失敗", ex);
        }
    }
}
