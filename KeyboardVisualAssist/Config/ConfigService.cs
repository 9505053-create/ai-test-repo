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

    // ── 標籤顯示模式 ──────────────────────────────────────
    /// <summary>All / EnglishOnly / TraditionalOnly / HsuOnly / EnglishAndHsu</summary>
    public string LabelMode { get; set; } = "All";

    // ── 視窗尺寸 ──────────────────────────────────────────
    /// <summary>Small / Medium / Large</summary>
    public string ScaleMode { get; set; } = "Small";

    // ── TargetAppsOnly 模式用 ────────────────────────────
    public List<string> TargetApps { get; set; } = new()
    {
        "WINWORD.EXE", "EXCEL.EXE", "OUTLOOK.EXE",
        "notepad.exe", "notepad++.exe", "POWERPNT.EXE"
    };
}

// ── 介面 ──────────────────────────────────────────────────────────────
/// <summary>
/// 配置服務介面
/// OverlayViewModel 僅依賴此介面，不直接碰 IO 細節。
/// </summary>
public interface IConfigService
{
    AppConfig Config { get; }

    /// <summary>
    /// 一般設定變更後呼叫：Debounce 300ms 後寫檔，
    /// 防止高頻 Toggle 連續觸發多次 IO。
    /// </summary>
    void SaveDebounced();

    /// <summary>
    /// 關閉或需要立即持久化時呼叫（跳過 Debounce）。
    /// </summary>
    void SaveImmediate();

    /// <summary>視窗位置持久化（只在 RememberWindowPosition = true 時寫入）。</summary>
    void SaveWindowPosition(double left, double top);
}

// ── 靜態 IO helpers（供 App.xaml.cs 啟動時使用）────────────────────────
public static class ConfigService
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    internal static string ConfigPath =>
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

// ── 實作類別 ───────────────────────────────────────────────────────────
/// <summary>
/// IConfigService 實作。
/// 內建 300ms Debounce：連續呼叫 SaveDebounced() 只觸發最後一次寫檔。
/// </summary>
public class ConfigManager : IConfigService, IDisposable
{
    public AppConfig Config { get; }

    private readonly System.Windows.Threading.DispatcherTimer _debounceTimer;
    private const int DebounceMs = 300;

    public ConfigManager(AppConfig config)
    {
        Config = config;

        _debounceTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ConfigService.Save(Config);
            AppLogger.Info("Config Debounce 寫檔完成");
        };
    }

    public void SaveDebounced()
    {
        // 重置計時器（每次呼叫都延後 300ms）
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void SaveImmediate()
    {
        _debounceTimer.Stop();
        ConfigService.Save(Config);
    }

    public void SaveWindowPosition(double left, double top)
    {
        if (!Config.RememberWindowPosition) return;
        Config.WindowLeft = left;
        Config.WindowTop  = top;
        SaveImmediate();   // 視窗位置在關閉時才呼叫，直接立即寫入
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
    }
}
