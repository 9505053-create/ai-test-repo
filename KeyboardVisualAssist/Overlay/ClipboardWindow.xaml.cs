using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// 剪貼簿監控小視窗
/// - 固定在 OverlayWindow 正上方
/// - 每 500ms 自動輪詢剪貼簿變化
/// - 有變化時自動更新顯示
/// </summary>
public partial class ClipboardWindow : Window
{
    private readonly Window _owner;
    private readonly DispatcherTimer _pollTimer;
    private string _lastClipboardText = "";

    // 輪詢間隔（ms）
    private const int PollIntervalMs = 500;

    public ClipboardWindow(Window owner)
    {
        InitializeComponent();
        _owner = owner;

        // 跟隨 owner 移動時重新定位
        _owner.LocationChanged += (_, _) => UpdatePosition();
        _owner.SizeChanged     += (_, _) => UpdatePosition();

        // 剪貼簿輪詢 Timer
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _pollTimer.Tick += OnPollTick;

        // 視窗載入後定位
        Loaded += (_, _) => UpdatePosition();
    }

    // ── 顯示 / 隱藏 ─────────────────────────────────────

    public new void Show()
    {
        ReadClipboard(force: true);
        UpdatePosition();
        base.Show();
        _pollTimer.Start();
        AppLogger.Info("剪貼簿監控視窗開啟");
    }

    public new void Hide()
    {
        _pollTimer.Stop();
        base.Hide();
        AppLogger.Info("剪貼簿監控視窗關閉");
    }

    // ── 位置：緊貼 OverlayWindow 正上方 ─────────────────

    private void UpdatePosition()
    {
        if (!_owner.IsVisible) return;

        // 計算 owner 的縮放比例
        double scale = 1.0;
        if (_owner.DataContext is OverlayViewModel vm)
            scale = vm.WindowScale;

        Left = _owner.Left;
        Top  = _owner.Top - ActualHeight - 4;  // 緊貼上方，留 4px 間距

        // 若超出螢幕上方，改放在 owner 下方
        if (Top < 0)
            Top = _owner.Top + _owner.ActualHeight + 4;
    }

    // ── 剪貼簿輪詢 ───────────────────────────────────────

    private void OnPollTick(object? sender, EventArgs e)
    {
        ReadClipboard(force: false);
    }

    private void ReadClipboard(bool force)
    {
        try
        {
            string text = GetClipboardDescription();

            bool changed = text != _lastClipboardText;
            if (changed || force)
            {
                _lastClipboardText = text;
                ContentText.Text = text;

                // 更新時間戳
                UpdateTimeText.Text = DateTime.Now.ToString("HH:mm:ss");

                // 有變化時閃爍指示燈（短暫變亮）
                if (changed && !force)
                {
                    AutoDot.Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88));
                    var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    resetTimer.Tick += (_, _) =>
                    {
                        resetTimer.Stop();
                        AutoDot.Fill = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x44, 0xBB, 0x44));
                    };
                    resetTimer.Start();

                    AppLogger.Info("剪貼簿內容已更新");
                }

                // 重新定位（內容高度可能改變）
                Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            ContentText.Text = "（無法讀取剪貼簿）";
            AppLogger.Error("讀取剪貼簿失敗", ex);
        }
    }

    /// <summary>
    /// 讀取剪貼簿，依照內容格式回傳描述字串。
    /// 支援：文字、圖片、檔案清單、其他格式。
    /// </summary>
    private static string GetClipboardDescription()
    {
        // 文字（最常見，優先）
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            return string.IsNullOrWhiteSpace(text) ? "（剪貼簿：空白文字）" : text;
        }

        // 圖片
        if (Clipboard.ContainsImage())
        {
            var img = Clipboard.GetImage();
            if (img != null)
                return $"[圖片]　{img.PixelWidth} × {img.PixelHeight} px";
            return "[圖片]（無法讀取尺寸）";
        }

        // 檔案/資料夾清單
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            if (files.Count == 1)
                return $"[檔案]　{files[0]}";
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"[檔案 ×{files.Count}]");
            foreach (string? f in files)
                if (f != null) lines.AppendLine($"  {System.IO.Path.GetFileName(f)}");
            return lines.ToString().TrimEnd();
        }

        // 其他格式（列出格式名稱）
        var data = Clipboard.GetDataObject();
        if (data != null)
        {
            var formats = data.GetFormats(autoConvert: false);
            if (formats.Length > 0)
                return $"[其他格式]\n  {string.Join("\n  ", formats.Take(5))}";
        }

        return "（剪貼簿無內容）";
    }

    // ── 拖曳 ─────────────────────────────────────────────

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ── 關閉按鈕 ─────────────────────────────────────────

    private void OnClose(object sender, RoutedEventArgs e)
    {
        // 通知 ViewModel 更新 ShowClipboard 狀態
        if (_owner.DataContext is OverlayViewModel vm)
            vm.ShowClipboard = false;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer.Stop();
        base.OnClosed(e);
    }
}
