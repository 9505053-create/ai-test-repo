using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// Overlay 視窗 Code-behind v1.1
/// - AlwaysVisible：啟動時固定右下角
/// - Lock/Unlock：動態切換 WS_EX_TRANSPARENT
/// - 不搶焦點：WS_EX_NOACTIVATE 永遠保持
/// </summary>
public partial class OverlayWindow : Window
{
    #region Win32 API

    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_NOACTIVATE  = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    #endregion

    private readonly OverlayViewModel _viewModel;
    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 套用初始 Opacity
        Opacity = viewModel.GetConfig().OverlayOpacity;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowStyles();
        PositionWindow();
    }

    // ── 視窗樣式 ─────────────────────────────────────────

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // 永遠套用：不搶焦點 + 不在 Alt+Tab 顯示
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

        // 初始依 config 決定是否 click-through
        if (_viewModel.IsLocked)
            exStyle |= WS_EX_TRANSPARENT;
        else
            exStyle &= ~WS_EX_TRANSPARENT;

        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>Lock/Unlock 切換 click-through（供外部呼叫）</summary>
    public void SetLocked(bool locked)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (locked)
            exStyle |= WS_EX_TRANSPARENT;
        else
            exStyle &= ~WS_EX_TRANSPARENT;

        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    // ── 視窗定位（右下角）───────────────────────────────

    private void PositionWindow()
    {
        var config = _viewModel.GetConfig();

        if (config.RememberWindowPosition &&
            config.WindowLeft >= 0 && config.WindowTop >= 0)
        {
            // 還原上次位置，並驗證是否在螢幕範圍內
            var screen = SystemParameters.WorkArea;
            double left = Math.Min(config.WindowLeft, screen.Width - Width - 8);
            double top  = Math.Min(config.WindowTop,  screen.Height - Height - 8);
            Left = Math.Max(0, left);
            Top  = Math.Max(0, top);
        }
        else
        {
            // 預設：右下角 + 8px 邊距
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

    // ── 拖曳（只有 Unlocked 時允許）─────────────────────

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
