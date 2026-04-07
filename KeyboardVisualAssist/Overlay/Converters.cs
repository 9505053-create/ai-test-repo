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
/// KeyCapState + IsModifier → Background Brush（三色主題）
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

    /// values[0]=DisplayState (KeyCapState), values[1]=IsModifier
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var state = values[0] is KeyCapState s ? s : KeyCapState.Normal;
        var isMod = values[1] is bool b && b;
        if (isMod)
            return state == KeyCapState.Pressed ? ModPressedBrush : ModNormalBrush;
        return state switch
        {
            KeyCapState.Pressed => PressedBrush,
            KeyCapState.Fading  => FadingBrush,
            _                   => NormalBrush
        };
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>KeyCapState → Border Brush（Pressed/Fading 時加亮邊框）</summary>
[ValueConversion(typeof(KeyCapState), typeof(Brush))]
public class FadeStateToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBorder = new(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush ActiveBorder = new(Color.FromRgb(0x00, 0xCC, 0xFF));
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is KeyCapState.Pressed or KeyCapState.Fading ? ActiveBorder : NormalBorder;
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
    /// values[0]=LabelMode(string), values[1]=LabelType
    /// LabelType: Primary / Traditional / Hsu / HsuShift / HsuShiftEng
    ///            CenterTraditional / CenterHsu
    /// 
    /// All:            Primary(中央) + Traditional(左上藍) + HsuShift(右上橙) + Hsu(左下紅)
    /// EnglishOnly:    Primary(中央)
    /// TraditionalOnly: CenterTraditional(中央大字藍)
    /// HsuOnly:        CenterHsu(中央大字紅) + CenterHsu Shift(右上橙)
    /// EnglishAndHsu:  Primary(中央) + Hsu(左下紅) + HsuShiftEng(右下橙)
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        var mode      = values[0] as string ?? "All";
        var labelType = values[1] as string ?? "Primary";
        bool visible = mode switch
        {
            "EnglishOnly"     => labelType == "Primary",
            "TraditionalOnly" => labelType == "CenterTraditional",
            "HsuOnly"         => labelType is "CenterHsu",
            "EnglishAndHsu"   => labelType is "Primary" or "Hsu" or "HsuShiftEng",
            // All：英文中央 + 傳統注音左上 + 許氏Shift右上 + 許氏主音左下
            _                 => labelType is "Primary" or "Traditional" or "HsuShift" or "Hsu"
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>bool → 中文字串（中/英 或 全/半）</summary>
public class BoolToStringConverter : IValueConverter
{
    public static readonly BoolToStringConverter ChineseEnglish = new() { TrueValue = "中", FalseValue = "英" };
    public static readonly BoolToStringConverter FullHalf       = new() { TrueValue = "全", FalseValue = "半" };

    public string TrueValue  { get; set; } = "是";
    public string FalseValue { get; set; } = "否";

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? TrueValue : FalseValue;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>bool → 前景色（亮/暗）</summary>
public class BoolToColorConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush ActiveBrush =
        new(System.Windows.Media.Color.FromRgb(0x00, 0xCC, 0x66));
    private static readonly System.Windows.Media.SolidColorBrush InactiveBrush =
        new(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));

    public static readonly BoolToColorConverter CapsLockColor = new();
    public static readonly BoolToColorConverter NumLockColor  = new();

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? ActiveBrush : InactiveBrush;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
