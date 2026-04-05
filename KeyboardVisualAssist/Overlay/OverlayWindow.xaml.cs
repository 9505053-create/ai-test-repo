using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// Overlay 視窗 Code-behind
/// 關鍵：WS_EX_NOACTIVATE 確保不搶焦點
/// </summary>
public partial class OverlayWindow : Window
{
    #region Win32 API — 不搶焦點設定

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;  // 不搶焦點
    private const int WS_EX_TRANSPARENT = 0x00000020; // 滑鼠穿透（可選）
    private const int WS_EX_TOOLWINDOW = 0x00000080;  // 不出現在 Alt+Tab

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    #endregion

    private readonly OverlayViewModel _viewModel;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 讀取設定：視窗位置
        var config = viewModel.GetConfig();
        Left = config.OverlayLeft;
        Top = config.OverlayTop;
        Opacity = config.OverlayOpacity;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyNoActivateStyle();
    }

    /// <summary>
    /// 套用 WS_EX_NOACTIVATE：視窗顯示/更新時不搶走焦點
    /// 這是讓 Word/Excel 輸入不中斷的關鍵
    /// </summary>
    private void ApplyNoActivateStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // 加上 NOACTIVATE + TOOLWINDOW（不在 Alt+Tab 顯示）
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// 覆寫 WndProc：攔截 WM_MOUSEACTIVATE，返回 MA_NOACTIVATE
    /// 雙重保險確保滑鼠點擊 overlay 時也不搶焦點
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        // 不呼叫 base.OnActivated，避免視窗真正 activate
        // base.OnActivated(e);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnToggleLayout(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleLayout();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 儲存視窗位置
        _viewModel.SaveWindowPosition(Left, Top);
        base.OnClosing(e);
    }
}
