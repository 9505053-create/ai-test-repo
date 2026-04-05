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
    private ForegroundAppMonitor? _monitor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Init();
        AppLogger.Info("KeyboardVisualAssist 啟動");

        try
        {
            // 載入設定
            var config = ConfigService.Load();
            AppLogger.Info($"設定載入完成，Layout: {config.ActiveLayout}");

            // 載入 KeyMap
            var repository = new KeyMapRepository();
            repository.LoadAll();

            // 初始化 ViewModel
            var viewModel = new OverlayViewModel(config, repository);

            // 初始化 Overlay 視窗
            _overlay = new OverlayWindow(viewModel);
            _overlay.Show();

            // 初始化 Hook
            _hook = new KeyboardHook();
            _hook.KeyEvent += (s, args) => viewModel.OnKeyEvent(args);
            _hook.Install();
            AppLogger.Info("鍵盤 Hook 安裝完成");

            // 初始化前景程式監控
            _monitor = new ForegroundAppMonitor(config.TargetApps);
            _monitor.AppChanged += (visible) =>
            {
                Dispatcher.Invoke(() => viewModel.IsOverlayVisible = visible);
            };
            _monitor.Start();
            AppLogger.Info("前景監控啟動完成");
        }
        catch (Exception ex)
        {
            AppLogger.Error("啟動失敗", ex);
            MessageBox.Show($"啟動失敗：{ex.Message}", "KeyboardVisualAssist", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Uninstall();
        _monitor?.Stop();
        AppLogger.Info("KeyboardVisualAssist 關閉");
        base.OnExit(e);
    }
}
