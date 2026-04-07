using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist;

public sealed class SystemTrayHelper : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;

    public SystemTrayHelper(
        Action toggleOverlay,
        Action cycleColorTheme,
        Action toggleLock,
        Action toggleView,
        Action cycleLabelMode,
        Action clearHighlight,
        Action exit)
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Keyboard Visual Assist",
            Visibility  = Visibility.Visible,
        };

        var menu = new System.Windows.Controls.ContextMenu();
        AddItem(menu, "顯示 / 隱藏",           toggleOverlay);
        AddItem(menu, "🔒 鎖定 / 🔓 解鎖",     toggleLock);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddItem(menu, "🎨 切換顏色主題",        cycleColorTheme);
        AddItem(menu, "切換標籤模式",            cycleLabelMode);
        AddItem(menu, "切換 Compact / Full",     toggleView);
        AddItem(menu, "⌫ 清除高亮",             clearHighlight);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddItem(menu, "結束",                   exit);

        _taskbarIcon.ContextMenu = menu;
        _taskbarIcon.TrayMouseDoubleClick += (s, e) => toggleOverlay();
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
