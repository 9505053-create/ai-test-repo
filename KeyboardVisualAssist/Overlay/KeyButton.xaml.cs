using System.Windows;
using System.Windows.Controls;

namespace KeyboardVisualAssist.Overlay;

public partial class KeyButton : UserControl
{
    private const double BaseKeyWidth = 38.0;

    public KeyButton()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is KeyCapViewModel vm)
            ApplyKeyStyle(vm);
    }

    private KeyCapViewModel? _vm;

    private void ApplyKeyStyle(KeyCapViewModel vm)
    {
        _vm = vm;

        // 鍵帽寬度
        KeyBorder.Width = vm.WidthUnit * BaseKeyWidth - 3;

        // 字體大小
        double fontSize = vm.IsModifier || vm.IsFunctionKey ? 9 : 11;
        MainText.FontSize = fontSize;

        // 寬鍵文字靠左
        if (vm.WidthUnit > 1.5)
        {
            MainText.HorizontalAlignment = HorizontalAlignment.Left;
            MainText.Margin = new Thickness(6, 0, 0, 0);
            MainText.FontSize = 9;
        }

        // 初始值
        UpdateCenterPhonetic(vm);

        // 監聽 KeyCapViewModel 標籤變化
        vm.PropertyChanged += (s, ev) =>
        {
            if (ev.PropertyName is nameof(KeyCapViewModel.SecondaryLabel)
                                 or nameof(KeyCapViewModel.TraditionalLabel))
                UpdateCenterPhonetic(vm);
        };

        // 監聽 LayoutMode 切換：延遲到 Loaded 後才能取得 Window
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_vm == null) return;
        var vm = _vm;

        if (Window.GetWindow(this)?.DataContext is OverlayViewModel ovm)
        {
            ovm.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(OverlayViewModel.LayoutMode))
                    UpdateCenterPhonetic(vm);
            };
        }
    }

    private void UpdateCenterPhonetic(KeyCapViewModel vm)
    {
        // 取得目前 LayoutMode（從 Window DataContext 的 OverlayViewModel）
        string layoutMode = "Standard";
        if (Window.GetWindow(this)?.DataContext is OverlayViewModel ovm)
            layoutMode = ovm.LayoutMode;

        // TraditionalOnly 模式（「注」）：
        //   Standard → 顯示傳統注音（TraditionalLabel，藍色）
        //   Hsu      → 顯示許氏主音（SecondaryLabel，但用藍色 TextBlock 顯示）
        TraditionalCenterText.Text = layoutMode == "Hsu"
            ? vm.SecondaryLabel
            : vm.TraditionalLabel;

        // HsuOnly 模式（「許」）：永遠顯示許氏主音
        HsuCenterText.Text = vm.SecondaryLabel;

        if (HsuShiftCenterText != null)
            HsuShiftCenterText.Text = vm.SecondaryShiftLabel;
    }
}
