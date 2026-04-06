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

    private void ApplyKeyStyle(KeyCapViewModel vm)
    {
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

        // CenterPhonetic：TraditionalOnly 顯示 Traditional，HsuOnly 顯示 Hsu
        // 用 DataContext Binding 自動更新，這裡設初始值
        UpdateCenterPhonetic(vm);
        vm.PropertyChanged += (s, ev) =>
        {
            if (ev.PropertyName is nameof(KeyCapViewModel.SecondaryLabel)
                                 or nameof(KeyCapViewModel.TraditionalLabel))
                UpdateCenterPhonetic(vm);
        };
        UpdateCenterPhonetic(vm);
    }

    private void UpdateCenterPhonetic(KeyCapViewModel vm)
    {
        // CenterPhoneticText 在 TraditionalOnly/HsuOnly 模式下為大字中央顯示
        // 實際顯示內容取決於當前 LabelMode，由 XAML DataTrigger 或 code-behind 切換
        // 這裡設置兩個 TextBlock 分別對應兩種模式
        TraditionalCenterText.Text = vm.TraditionalLabel;
        HsuCenterText.Text = vm.SecondaryLabel;
    }
}
