using System.Windows;
using KeyboardVisualAssist.Config;
using KeyboardVisualAssist.InputCapture;
using KeyboardVisualAssist.KeyMap;
using KeyboardVisualAssist.Monitor;
using KeyboardVisualAssist.Overlay;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist;

public partial class App : Application
{
    private KeyboardHook? _hook;
    private OverlayWindow? _overlay;
    private OverlayViewModel? _viewModel;
    private ForegroundAppMonitor? _monitor;
    private SystemTrayHelper? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Init();
        AppLogger.Info("KeyboardVisualAssist v1.1 啟動");

        try
        {
            var config = ConfigService.Load();
            AppLogger.Info($"設定載入：DisplayMode={config.DisplayMode}, Layout={config.LayoutMode}");

            var repository = new KeyMapRepository();
            repository.LoadAll();

            _viewModel = new OverlayViewModel(config, repository);
            _overlay = new OverlayWindow(_viewModel);
            _overlay.Show();

            // 鍵盤 Hook
            _hook = new KeyboardHook();
            _hook.KeyEvent += (s, args) => _viewModel.OnKeyEvent(args);
            _hook.Install();
            AppLogger.Info("鍵盤 Hook 安裝完成");

            // DisplayMode 決定是否啟動前景監控
            if (config.DisplayMode == "TargetAppsOnly")
            {
                _monitor = new ForegroundAppMonitor(config.TargetApps);
                _monitor.AppChanged += (visible) =>
                    Dispatcher.Invoke(() => _viewModel.IsOverlayVisible = visible);
                _monitor.Start();
                AppLogger.Info("TargetAppsOnly 模式：前景監控啟動");
            }
            else
            {
                // AlwaysVisible：Overlay 永遠顯示
                _viewModel.IsOverlayVisible = true;
                AppLogger.Info("AlwaysVisible 模式：Overlay 常駐顯示");
            }

            // 系統匣
            _tray = new SystemTrayHelper(
                toggleOverlay:  () => Dispatcher.Invoke(ToggleOverlayVisible),
                toggleLayout:   () => Dispatcher.Invoke(() => _viewModel.ToggleLayout()),
                toggleLock:     () => Dispatcher.Invoke(ToggleLock),
                toggleView:     () => Dispatcher.Invoke(() => _viewModel.ToggleViewMode()),
                exit:           () => Dispatcher.Invoke(Shutdown)
            );
        }
        catch (Exception ex)
        {
            AppLogger.Error("啟動失敗", ex);
            MessageBox.Show($"啟動失敗：{ex.Message}", "KeyboardVisualAssist",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ToggleOverlayVisible()
    {
        if (_viewModel == null) return;
        _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;
    }

    private void ToggleLock()
    {
        if (_viewModel == null || _overlay == null) return;
        _viewModel.ToggleLock();
        _overlay.SetLocked(_viewModel.IsLocked);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Uninstall();
        _monitor?.Stop();
        _tray?.Dispose();
        AppLogger.Info("KeyboardVisualAssist 關閉");
        base.OnExit(e);
    }
}
