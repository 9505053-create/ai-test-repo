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
/// FadeState + IsModifier → Background Brush（三色主題）
/// 英文/功能鍵 = 暗灰底，高亮=藍
/// 修飾鍵 = 深色底，高亮=橙
/// </summary>
public class FadeStateToBrushConverter : IMultiValueConverter
{
    public static readonly SolidColorBrush NormalBrush     = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    public static readonly SolidColorBrush PressedBrush    = new(Color.FromRgb(0x00, 0xAA, 0xFF));
    public static readonly SolidColorBrush FadingBrush     = new(Color.FromRgb(0x00, 0x66, 0x99));
    public static readonly SolidColorBrush ModNormalBrush  = new(Color.FromRgb(0x22, 0x22, 0x22));
    public static readonly SolidColorBrush ModPressedBrush = new(Color.FromRgb(0xFF, 0x88, 0x00));

    /// values[0]=DisplayFadeState, values[1]=IsModifier
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var state = values[0] is FadeState fs ? fs : FadeState.Normal;
        var isMod = values[1] is bool b && b;
        if (isMod)
            return state == FadeState.Pressed ? ModPressedBrush : ModNormalBrush;
        return state switch
        {
            FadeState.Pressed => PressedBrush,
            FadeState.Fading  => FadingBrush,
            _                 => NormalBrush
        };
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>FadeState → Border Brush（Pressed/Fading 時加亮邊框）</summary>
[ValueConversion(typeof(FadeState), typeof(Brush))]
public class FadeStateToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBorder = new(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush ActiveBorder = new(Color.FromRgb(0x00, 0xCC, 0xFF));
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is FadeState.Pressed or FadeState.Fading ? ActiveBorder : NormalBorder;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Row index → Canvas Y 座標（每列 38px）</summary>
[ValueConversion(typeof(int), typeof(double))]
public class RowToYConverter : IValueConverter
{
    private static readonly double[] RowY = { 0, 38, 76, 114, 152 };
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int row && row >= 0 && row < RowY.Length ? RowY[row] : 0.0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Col 浮點偏移 → Canvas X 座標（基礎單位 38px）</summary>
[ValueConversion(typeof(double), typeof(double))]
public class ColToXConverter : IValueConverter
{
    private const double BaseUnit = 38.0;
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        double col = value switch
        {
            double d => d,
            int i    => (double)i,
            _        => 0.0
        };
        return col * BaseUnit;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>WidthUnit → 實際像素寬度（基礎單位 38px，扣掉 Margin 3px）</summary>
[ValueConversion(typeof(double), typeof(double))]
public class WidthUnitToPixelConverter : IValueConverter
{
    private const double BaseUnit = 38.0;
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is double w ? w * BaseUnit - 3 : BaseUnit - 3;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>string 非空 → Visible，空 → Collapsed</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>LabelMode + 標籤種類 → Visibility（控制每層標籤顯示）</summary>
public class LabelModeToVisibilityConverter : IMultiValueConverter
{
    /// values[0]=LabelMode(string), values[1]=LabelType(string: "Primary"/"Traditional"/"Hsu"/"CenterPhonetic")
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        var mode      = values[0] as string ?? "All";
        var labelType = values[1] as string ?? "Primary";
        bool visible = mode switch
        {
            "EnglishOnly"     => labelType == "Primary",
            "TraditionalOnly" => labelType is "CenterPhonetic",
            "HsuOnly"         => labelType is "CenterPhonetic",
            "EnglishAndHsu"   => labelType is "Primary" or "Hsu",
            // "All": 顯示 Primary + Hsu(左上紅) + Traditional(右上藍)，不顯示 CenterPhonetic
            _                 => labelType != "CenterPhonetic"
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}
