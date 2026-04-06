using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeyboardVisualAssist.Overlay;

public partial class OverlayWindow : Window
{
    #region Win32 API
    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_NCHITTEST     = 0x0084;
    private const int HTCLIENT         = 1;
    private const int HTTRANSPARENT    = -1;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int n, int v);
    #endregion

    private readonly OverlayViewModel _viewModel;
    private const double ControlBarHeight = 34.0;

    // 縮小前的位置（還原用）
    private double _savedLeft, _savedTop;
    private bool _isMinimized = false;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Opacity = viewModel.GetConfig().OverlayOpacity;

        // 視窗尺寸依 ScaleMode 初始化
        ApplyBaseSize();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyWindowStyles();
        PositionWindow();
    }

    // ── 視窗尺寸 ─────────────────────────────────────────

    private void ApplyBaseSize()
    {
        // 固定 base 尺寸，縮放由 LayoutTransform 的 WindowScale 控制
        Width  = 590;
        Height = 265;
    }

    // ── WM_NCHITTEST：分區穿透 ───────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            var pt = PointFromScreen(new Point(screenX, screenY));

            // 標題列 + 底部條：永遠可點擊
            if (pt.Y <= ControlBarHeight || pt.Y >= ActualHeight - 30)
            {
                handled = true;
                return new IntPtr(HTCLIENT);
            }

            // 鍵盤主體：Locked 時穿透
            if (_viewModel.IsLocked)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
        }
        return IntPtr.Zero;
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        exStyle &= ~0x00000020; // 清除 WS_EX_TRANSPARENT
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    public void SetLocked(bool locked) { /* WndProc 即時判斷，無需額外操作 */ }

    // ── 位置 ─────────────────────────────────────────────

    private void PositionWindow()
    {
        var config = _viewModel.GetConfig();
        if (config.RememberWindowPosition && config.WindowLeft >= 0 && config.WindowTop >= 0)
        {
            var screen = SystemParameters.WorkArea;
            Left = Math.Max(0, Math.Min(config.WindowLeft, screen.Width  - Width  - 8));
            Top  = Math.Max(0, Math.Min(config.WindowTop,  screen.Height - Height - 8));
        }
        else
        {
            var screen = SystemParameters.WorkArea;
            Left = screen.Right  - Width  - 8;
            Top  = screen.Bottom - Height - 8;
        }
    }

    protected override void OnActivated(EventArgs e) { /* 不搶焦點 */ }

    // ── 拖曳：標題列任何位置都可拖 ──────────────────────

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // 阻止按鈕區域的 MouseLeftButtonDown 冒泡到標題列的拖曳
    private void StopDragPropagation(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    // ── 按鈕事件 ─────────────────────────────────────────

    private void OnToggleLayout(object sender, RoutedEventArgs e)
        => _viewModel.ToggleLayout();

    private void OnToggleLock(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleLock();
        SetLocked(_viewModel.IsLocked);
    }

    private void OnToggleView(object sender, RoutedEventArgs e)
        => _viewModel.ToggleViewMode();

    private void OnCycleLabelMode(object sender, RoutedEventArgs e)
        => _viewModel.CycleLabelMode();

    private void OnCycleScale(object sender, RoutedEventArgs e)
        => _viewModel.CycleScaleMode();

    private void OnClearHighlight(object sender, RoutedEventArgs e)
        => _viewModel.ClearHighlight();

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (_isMinimized)
        {
            // 還原
            Width  = 590;
            Height = 265;
            Left   = _savedLeft;
            Top    = _savedTop;
            _isMinimized = false;
        }
        else
        {
            // 縮小為標題列
            _savedLeft = Left;
            _savedTop  = Top;
            Width  = 200;
            Height = 38;
            _isMinimized = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveWindowPosition(Left, Top);
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.SaveWindowPosition(Left, Top);
        base.OnClosing(e);
    }
}
