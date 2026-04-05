using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// Overlay 視窗 Code-behind v1.2
/// 核心改進：WM_NCHITTEST 攔截
/// - 控制列（標題+按鈕）永遠可點擊
/// - 鍵盤主體區域在 Locked 時 click-through
/// - WS_EX_TRANSPARENT 完全廢棄，改用 HitTest 方案
/// </summary>
public partial class OverlayWindow : Window
{
    #region Win32 API

    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_NOACTIVATE  = 0x08000000;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;

    private const int WM_NCHITTEST      = 0x0084;
    private const int HTCLIENT          = 1;
    private const int HTTRANSPARENT     = -1;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    #endregion

    private readonly OverlayViewModel _viewModel;

    // 控制列高度（標題列 + margin）
    // 滑鼠落在這個高度以內 → 永遠回 HTCLIENT（可點擊）
    private const double ControlBarHeight = 34.0;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Opacity = viewModel.GetConfig().OverlayOpacity;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 掛載 WndProc
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        ApplyWindowStyles();
        PositionWindow();
    }

    // ── WndProc：攔截 HitTest ─────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            // 取得滑鼠相對視窗的 Y 座標（DIP）
            // lParam 低16位 = 螢幕X，高16位 = 螢幕Y
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            // 轉換為視窗內部座標
            var pt = PointFromScreen(new Point(screenX, screenY));

            // 控制列區域（頂部 ControlBarHeight px）→ 永遠 HTCLIENT
            if (pt.Y <= ControlBarHeight)
            {
                handled = true;
                return new IntPtr(HTCLIENT);
            }

            // 底部 RecentKeys 條（最後 30px）→ 永遠 HTCLIENT
            if (pt.Y >= ActualHeight - 30)
            {
                handled = true;
                return new IntPtr(HTCLIENT);
            }

            // 鍵盤主體區域：Locked = 穿透，Unlocked = 可互動
            if (_viewModel.IsLocked)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
        }

        return IntPtr.Zero;
    }

    // ── 視窗樣式（移除 WS_EX_TRANSPARENT）──────────────────

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // 永遠套用：不搶焦點 + 不在 Alt+Tab 顯示
        // 注意：完全移除 WS_EX_TRANSPARENT，改由 WM_NCHITTEST 控制穿透
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        exStyle &= ~0x00000020; // 確保清除 WS_EX_TRANSPARENT

        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>Lock/Unlock — 現在只需更新 ViewModel，WndProc 會自動生效</summary>
    public void SetLocked(bool locked)
    {
        // WM_NCHITTEST 方案不需要修改 ExStyle，locked 狀態由 _viewModel.IsLocked 即時判斷
        // 此方法保留供 App.xaml.cs 呼叫介面相容
    }

    // ── 視窗定位 ─────────────────────────────────────────

    private void PositionWindow()
    {
        var config = _viewModel.GetConfig();

        if (config.RememberWindowPosition &&
            config.WindowLeft >= 0 && config.WindowTop >= 0)
        {
            var screen = SystemParameters.WorkArea;
            double left = Math.Min(config.WindowLeft, screen.Width  - Width  - 8);
            double top  = Math.Min(config.WindowTop,  screen.Height - Height - 8);
            Left = Math.Max(0, left);
            Top  = Math.Max(0, top);
        }
        else
        {
            var screen = SystemParameters.WorkArea;
            Left = screen.Right  - Width  - 8;
            Top  = screen.Bottom - Height - 8;
        }
    }

    // ── 不搶焦點保護 ─────────────────────────────────────

    protected override void OnActivated(EventArgs e)
    {
        // 不呼叫 base，避免視窗真正 Activate
    }

    // ── 拖曳（Unlocked 時，滑鼠在標題列拖曳）────────────

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsLocked && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ── 控制按鈕事件 ─────────────────────────────────────

    private void OnToggleLayout(object sender, RoutedEventArgs e)
        => _viewModel.ToggleLayout();

    private void OnToggleLock(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleLock();
        SetLocked(_viewModel.IsLocked);
    }

    private void OnToggleView(object sender, RoutedEventArgs e)
        => _viewModel.ToggleViewMode();

    // ── 關閉 ─────────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.SaveWindowPosition(Left, Top);
        base.OnClosing(e);
    }
}
