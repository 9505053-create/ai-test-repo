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
    private ConfigManager? _configManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Init();
        AppLogger.Info("KeyboardVisualAssist v1.4 啟動");

        try
        {
            var config      = ConfigService.Load();
            _configManager  = new ConfigManager(config);

            var repository  = new KeyMapRepository();
            repository.LoadAll();
            AppLogger.Info($"KeyMap 載入完成，共 {repository.AllEntries.Count} 顆按鍵");

            var keymapSvc   = new KeymapService(repository);

            _viewModel = new OverlayViewModel(_configManager, keymapSvc, repository);
            _overlay   = new OverlayWindow(_viewModel);
            _overlay.Show();
            AppLogger.Info($"Overlay 視窗已顯示，Log 路徑: {AppLogger.CurrentLogPath}");

            _hook = new KeyboardHook();
            _hook.KeyEvent += (s, args) => _viewModel.OnKeyEvent(args);
            _hook.Install();

            if (config.DisplayMode == "TargetAppsOnly")
            {
                _monitor = new ForegroundAppMonitor(config.TargetApps);
                _monitor.AppChanged += visible =>
                    Dispatcher.Invoke(() => _viewModel.IsOverlayVisible = visible);
                _monitor.Start();
            }
            else
            {
                _viewModel.IsOverlayVisible = true;
            }

            _tray = new SystemTrayHelper(
                toggleOverlay:   () => Dispatcher.Invoke(ToggleOverlay),
                restoreFromTray: () => Dispatcher.Invoke(() => _overlay?.RestoreFromTray()),
                cycleColorTheme: () => Dispatcher.Invoke(() => _viewModel.CycleColorTheme()),
                toggleLock:      () => Dispatcher.Invoke(ToggleLock),
                toggleView:      () => Dispatcher.Invoke(() => _viewModel.ToggleViewMode()),
                cycleLabelMode:  () => Dispatcher.Invoke(() => _viewModel.CycleLabelMode()),
                clearHighlight:  () => Dispatcher.Invoke(() => _viewModel.ClearHighlight()),
                exit:            () => Dispatcher.Invoke(Shutdown)
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

    private void ToggleOverlay()
    {
        if (_viewModel != null)
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
        _viewModel?.StatusMonitor.Stop();
        _tray?.Dispose();
        // 關閉時確保立即寫入（跳過 Debounce）
        _configManager?.SaveImmediate();
        _configManager?.Dispose();
        AppLogger.Info("KeyboardVisualAssist 關閉");
        base.OnExit(e);
    }
}
