using System.Windows;
using System.Windows.Forms;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist;

/// <summary>
/// 系統匣常駐圖示
/// 提供右鍵選單：顯示/隱藏 Overlay、切換 Layout、結束程式
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _toggleOverlay;
    private readonly Action _toggleLayout;
    private readonly Action _exit;

    public SystemTrayHelper(Action toggleOverlay, Action toggleLayout, Action exit)
    {
        _toggleOverlay = toggleOverlay;
        _toggleLayout = toggleLayout;
        _exit = exit;

        _notifyIcon = new NotifyIcon
        {
            Text = "Keyboard Visual Assist",
            Visible = true,
            // Icon 使用預設（可替換為自訂 .ico）
            Icon = System.Drawing.SystemIcons.Application
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("顯示/隱藏 Overlay", null, (s, e) => _toggleOverlay());
        menu.Items.Add("切換 Standard / Hsu", null, (s, e) => _toggleLayout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束", null, (s, e) => _exit());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => _toggleOverlay();

        AppLogger.Info("系統匣圖示建立完成");
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
