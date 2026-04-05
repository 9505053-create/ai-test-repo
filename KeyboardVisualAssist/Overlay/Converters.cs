using System.Globalization;
using System.Windows;
using System.Windows.Data;

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
/// Row index → Canvas Y 座標
/// Row 0~4 對應鍵盤的 5 列
/// </summary>
[ValueConversion(typeof(int), typeof(double))]
public class RowToYConverter : IValueConverter
{
    // 每列的 Y 偏移（px），對應鍵盤圖版面
    private static readonly double[] RowY = { 0, 38, 76, 114, 152 };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int row && row >= 0 && row < RowY.Length)
            return RowY[row];
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Col index → Canvas X 座標
/// 基礎單位 38px，Col 0 對齊行首
/// </summary>
[ValueConversion(typeof(int), typeof(double))]
public class ColToXConverter : IValueConverter
{
    private const double BaseUnit = 38.0;

    // 每列的 X 偏移（部分列有縮排，例如 ASDF 列）
    // Row 偏移透過 RowOffset 處理，這裡只計算列內的 Col 偏移
    // 實際偏移由 KeyMapEntry.Col 累加寬度決定（在 Repository 後處理）
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int col)
            return col * BaseUnit;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
