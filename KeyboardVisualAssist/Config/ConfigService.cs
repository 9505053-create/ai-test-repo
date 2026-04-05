using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Config;

/// <summary>應用程式設定模型</summary>
public class AppConfig
{
    /// <summary>Overlay 左上角 X 座標</summary>
    public double OverlayLeft { get; set; } = 20;

    /// <summary>Overlay 左上角 Y 座標（-1 = 自動置底）</summary>
    public double OverlayTop { get; set; } = -1;

    /// <summary>Overlay 透明度 (0.0 ~ 1.0)</summary>
    public double OverlayOpacity { get; set; } = 0.88;

    /// <summary>字體大小</summary>
    public int FontSize { get; set; } = 12;

    /// <summary>高亮持續時間（ms），之後淡出</summary>
    public int HighlightDurationMs { get; set; } = 500;

    /// <summary>最近按鍵顯示數量</summary>
    public int RecentKeyCount { get; set; } = 5;

    /// <summary>是否顯示鍵盤圖</summary>
    public bool ShowKeyboardMap { get; set; } = true;

    /// <summary>目前啟用的 layout：Standard / Hsu</summary>
    public string ActiveLayout { get; set; } = "Standard";

    /// <summary>指定顯示 overlay 的前景程式清單</summary>
    public List<string> TargetApps { get; set; } = new()
    {
        "WINWORD.EXE",
        "EXCEL.EXE",
        "OUTLOOK.EXE",
        "notepad.exe",
        "notepad++.exe",
        "POWERPNT.EXE"
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
        Save(def); // 自動建立預設設定檔
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
