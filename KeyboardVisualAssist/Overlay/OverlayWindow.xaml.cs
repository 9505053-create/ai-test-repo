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
    private const double ControlBarHeight = 36.0;  // 標題列高度（含 scale）

    private double _savedLeft, _savedTop;
    private bool _isMinimized = false;

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
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyWindowStyles();
        PositionWindow();
    }

    // ── WM_NCHITTEST 分區穿透 ─────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            int sx = (short)(lParam.ToInt32() & 0xFFFF);
            int sy = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            var pt = PointFromScreen(new Point(sx, sy));

            // 標題列（頂部）或底部條 → 永遠可點擊
            double scaledCtrlHeight = ControlBarHeight * _viewModel.WindowScale;
            if (pt.Y <= scaledCtrlHeight || pt.Y >= ActualHeight - 24 * _viewModel.WindowScale)
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
        exStyle &= ~0x00000020;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    public void SetLocked(bool locked) { /* WndProc 即時判斷 */ }

    // ── 位置 ─────────────────────────────────────────────

    private void PositionWindow()
    {
        var cfg = _viewModel.GetConfig();
        var screen = SystemParameters.WorkArea;
        if (cfg.RememberWindowPosition && cfg.WindowLeft >= 0 && cfg.WindowTop >= 0)
        {
            Left = Math.Max(0, Math.Min(cfg.WindowLeft, screen.Width  - ActualWidth  - 8));
            Top  = Math.Max(0, Math.Min(cfg.WindowTop,  screen.Height - ActualHeight - 8));
        }
        else
        {
            // 右下角定位（等 SizeToContent 算完才能取 ActualWidth）
            Dispatcher.BeginInvoke(() =>
            {
                Left = screen.Right  - ActualWidth  - 8;
                Top  = screen.Bottom - ActualHeight - 8;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    protected override void OnActivated(EventArgs e) { /* 不搶焦點 */ }

    // ── 拖曳 ─────────────────────────────────────────────

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void StopDragPropagation(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    // ── 按鈕事件 ─────────────────────────────────────────

    private void OnToggleLayout(object sender, RoutedEventArgs e)
        => _viewModel.ToggleLayout();

    private void OnToggleLock(object sender, RoutedEventArgs e)
        => _viewModel.ToggleLock();

    private void OnToggleView(object sender, RoutedEventArgs e)
        => _viewModel.ToggleViewMode();

    private void OnCycleLabelMode(object sender, RoutedEventArgs e)
        => _viewModel.CycleLabelMode();

    private void OnCycleScale(object sender, RoutedEventArgs e)
        => _viewModel.CycleScaleMode();

    private void OnClearHighlight(object sender, RoutedEventArgs e)
        => _viewModel.ClearHighlight();

    private void OnIncreaseOpacity(object sender, RoutedEventArgs e)
        => _viewModel.IncreaseOpacity();

    private void OnDecreaseOpacity(object sender, RoutedEventArgs e)
        => _viewModel.DecreaseOpacity();

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (_isMinimized)
        {
            RootBorder.Visibility = Visibility.Visible;
            Left = _savedLeft;
            Top  = _savedTop;
            _isMinimized = false;
        }
        else
        {
            _savedLeft = Left;
            _savedTop  = Top;
            RootBorder.Visibility = Visibility.Collapsed;
            // 縮成小條
            Width  = 120;
            Height = 28;
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
