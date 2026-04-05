using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace KeyboardVisualAssist.Overlay;

/// <summary>bool → Visibility</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// FadeState → Background Brush
/// Normal=暗灰, Pressed=亮藍/橙, Fading=中間色
/// IsModifier 由外部決定高亮色
/// </summary>
public class FadeStateToBrushConverter : IMultiValueConverter
{
    // 一般鍵
    public static readonly SolidColorBrush NormalBrush    = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    public static readonly SolidColorBrush PressedBrush   = new(Color.FromRgb(0x00, 0xAA, 0xFF));
    public static readonly SolidColorBrush FadingBrush    = new(Color.FromRgb(0x00, 0x66, 0x99));
    // 修飾鍵
    public static readonly SolidColorBrush ModPressedBrush = new(Color.FromRgb(0xFF, 0x88, 0x00));
    // 寬鍵底色
    public static readonly SolidColorBrush WideBrush      = new(Color.FromRgb(0x22, 0x22, 0x22));

    /// <summary>
    /// values[0] = DisplayFadeState (FadeState)
    /// values[1] = IsModifier (bool)
    /// </summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return NormalBrush;
        var state     = values[0] is FadeState fs ? fs : FadeState.Normal;
        var isMod     = values[1] is bool b && b;

        if (isMod)
            return state == FadeState.Pressed ? ModPressedBrush : WideBrush;

        return state switch
        {
            FadeState.Pressed => PressedBrush,
            FadeState.Fading  => FadingBrush,
            _                 => NormalBrush
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// FadeState → Border Glow Effect（Pressed 時加亮邊框）
/// </summary>
[ValueConversion(typeof(FadeState), typeof(Brush))]
public class FadeStateToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBorder  = new(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush ActiveBorder  = new(Color.FromRgb(0x00, 0xCC, 0xFF));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FadeState.Pressed or FadeState.Fading ? ActiveBorder : NormalBorder;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Row index → Canvas Y 座標
/// </summary>
[ValueConversion(typeof(int), typeof(double))]
public class RowToYConverter : IValueConverter
{
    private static readonly double[] RowY = { 0, 38, 76, 114, 152 };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int row && row >= 0 && row < RowY.Length ? RowY[row] : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Col index → Canvas X 座標（基礎單位 38px）
/// </summary>
[ValueConversion(typeof(int), typeof(double))]
public class ColToXConverter : IValueConverter
{
    private const double BaseUnit = 38.0;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int col ? col * BaseUnit : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// WidthUnit → 實際像素寬度
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public class WidthUnitToPixelConverter : IValueConverter
{
    private const double BaseUnit = 38.0;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double w ? w * BaseUnit - 3 : BaseUnit - 3; // -3 for margin
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// SecondaryLabel 是否有內容 → Visibility
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
