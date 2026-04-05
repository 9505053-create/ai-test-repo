using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist;

/// <summary>
/// 系統匣常駐圖示 v1.1
/// 選單：顯示/隱藏、鎖定/解鎖、Layout、Compact/Full、結束
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly Action _toggleOverlay;
    private readonly Action _toggleLayout;
    private readonly Action _toggleLock;
    private readonly Action _toggleView;
    private readonly Action _exit;

    public SystemTrayHelper(
        Action toggleOverlay,
        Action toggleLayout,
        Action toggleLock,
        Action toggleView,
        Action exit)
    {
        _toggleOverlay = toggleOverlay;
        _toggleLayout  = toggleLayout;
        _toggleLock    = toggleLock;
        _toggleView    = toggleView;
        _exit          = exit;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Keyboard Visual Assist",
            Visibility  = Visibility.Visible,
        };

        var menu = new System.Windows.Controls.ContextMenu();

        AddItem(menu, "顯示 / 隱藏",          _toggleOverlay);
        AddItem(menu, "🔒 鎖定 / 🔓 解鎖",    _toggleLock);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddItem(menu, "切換 Standard / Hsu",   _toggleLayout);
        AddItem(menu, "切換 Compact / Full",    _toggleView);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddItem(menu, "結束",                  _exit);

        _taskbarIcon.ContextMenu = menu;
        _taskbarIcon.TrayMouseDoubleClick += (s, e) => _toggleOverlay();

        AppLogger.Info("系統匣圖示建立完成");
    }

    private static void AddItem(System.Windows.Controls.ContextMenu menu, string header, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (s, e) => action();
        menu.Items.Add(item);
    }

    public void Dispose() => _taskbarIcon.Dispose();
}
