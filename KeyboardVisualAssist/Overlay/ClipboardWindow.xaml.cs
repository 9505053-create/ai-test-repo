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
            string text = "";
            if (Clipboard.ContainsText())
                text = Clipboard.GetText();

            bool changed = text != _lastClipboardText;
            if (changed || force)
            {
                _lastClipboardText = text;
                ContentText.Text = string.IsNullOrEmpty(text)
                    ? "（剪貼簿無文字內容）"
                    : text;

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

    protected override void OnClosed(System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer.Stop();
    }
}
