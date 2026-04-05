using System.Windows;
using System.Windows.Controls;
using KeyboardVisualAssist.Overlay;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// KeyButton Code-behind v1.1
/// DataContext = KeyCapViewModel（由 OverlayWindow XAML 透過 ItemsControl 綁定）
/// 寬度由 WidthUnit 決定，其餘全部透過 Binding 驅動
/// </summary>
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
            ApplyWidth(vm);
    }

    private void ApplyWidth(KeyCapViewModel vm)
    {
        // 寬度由 WidthUnit 計算，含 Margin 3px（左右各 1.5）
        KeyBorder.Width = vm.WidthUnit * BaseKeyWidth - 3;

        // 修飾鍵/功能鍵用稍小字體
        if (vm.IsModifier || vm.IsFunctionKey)
            MainText.FontSize = 9;
        else
            MainText.FontSize = 11;

        // 寬鍵（WidthUnit > 1.5）文字靠左
        if (vm.WidthUnit > 1.5)
        {
            MainText.HorizontalAlignment = HorizontalAlignment.Left;
            MainText.Margin = new Thickness(6, 0, 0, 0);
            MainText.FontSize = 9;
        }
    }
}
