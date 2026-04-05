// 此檔案是 OverlayViewModel 的補充 partial，
// 放置 OverlayWindow.xaml.cs 需要呼叫的輔助方法。
// 實際整合時請合併到 OverlayViewModel.cs

using KeyboardVisualAssist.Config;

namespace KeyboardVisualAssist.Overlay;

public partial class OverlayViewModel
{
    public AppConfig GetConfig() => _config;

    public void SaveWindowPosition(double left, double top)
    {
        _config.OverlayLeft = left;
        _config.OverlayTop = top;
        ConfigService.Save(_config);
    }
}
