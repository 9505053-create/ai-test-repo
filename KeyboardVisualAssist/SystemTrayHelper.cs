using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist;

/// <summary>
/// 系統匣常駐圖示
/// 提供右鍵選單：顯示/隱藏 Overlay、切換 Layout、結束程式
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly Action _toggleOverlay;
    private readonly Action _toggleLayout;
    private readonly Action _exit;

    public SystemTrayHelper(Action toggleOverlay, Action toggleLayout, Action exit)
    {
        _toggleOverlay = toggleOverlay;
        _toggleLayout = toggleLayout;
        _exit = exit;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Keyboard Visual Assist",
            Visibility = Visibility.Visible,
        };

        var menu = new System.Windows.Controls.ContextMenu();

        var itemToggle = new System.Windows.Controls.MenuItem { Header = "顯示/隱藏 Overlay" };
        itemToggle.Click += (s, e) => _toggleOverlay();

        var itemLayout = new System.Windows.Controls.MenuItem { Header = "切換 Standard / Hsu" };
        itemLayout.Click += (s, e) => _toggleLayout();

        var itemExit = new System.Windows.Controls.MenuItem { Header = "結束" };
        itemExit.Click += (s, e) => _exit();

        menu.Items.Add(itemToggle);
        menu.Items.Add(itemLayout);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(itemExit);

        _taskbarIcon.ContextMenu = menu;
        _taskbarIcon.TrayMouseDoubleClick += (s, e) => _toggleOverlay();

        AppLogger.Info("系統匣圖示建立完成");
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }
}
