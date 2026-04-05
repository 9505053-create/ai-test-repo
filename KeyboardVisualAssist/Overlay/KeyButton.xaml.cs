using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeyboardVisualAssist.KeyMap;

namespace KeyboardVisualAssist.Overlay;

/// <summary>
/// 鍵盤上的單一按鍵控制項
/// 支援高亮 + 淡出動畫
/// 支援 Standard / Hsu 雙模式顯示
/// </summary>
public partial class KeyButton : UserControl
{
    // ========== Dependency Properties ==========

    public static readonly DependencyProperty HighlightedKeysProperty =
        DependencyProperty.Register(
            nameof(HighlightedKeys),
            typeof(ObservableCollection<string>),
            typeof(KeyButton),
            new PropertyMetadata(null, OnHighlightedKeysChanged));

    public static readonly DependencyProperty ShowHsuProperty =
        DependencyProperty.Register(
            nameof(ShowHsu),
            typeof(bool),
            typeof(KeyButton),
            new PropertyMetadata(true, OnShowHsuChanged));

    public ObservableCollection<string>? HighlightedKeys
    {
        get => (ObservableCollection<string>?)GetValue(HighlightedKeysProperty);
        set => SetValue(HighlightedKeysProperty, value);
    }

    public bool ShowHsu
    {
        get => (bool)GetValue(ShowHsuProperty);
        set => SetValue(ShowHsuProperty, value);
    }

    // ========== 顏色定義 ==========
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0x00, 0xAA, 0xFF));
    private static readonly SolidColorBrush ModifierHighlightBrush = new(Color.FromRgb(0xFF, 0x88, 0x00));
    private static readonly SolidColorBrush WideBrush = new(Color.FromRgb(0x22, 0x22, 0x22));

    private KeyMapEntry? _entry;
    private Storyboard? _fadeStoryboard;
    private const int KeyUnitWidth = 38; // 基礎鍵寬（px）

    public KeyButton()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is KeyMapEntry entry)
        {
            _entry = entry;
            ApplyEntry(entry);
        }
    }

    private void ApplyEntry(KeyMapEntry entry)
    {
        // 設定寬度
        double width = KeyUnitWidth * entry.WidthMultiplier;
        KeyBorder.Width = width;

        // 主標籤
        MainText.Text = entry.IsWideKey ? entry.StandardLabel : entry.StandardLabel;

        // 寬鍵（Shift/Enter/Backspace 等）用稍暗背景
        if (entry.IsWideKey || entry.IsModifier || entry.IsFunctionKey)
            KeyBorder.Background = WideBrush;
        else
            KeyBorder.Background = NormalBrush;

        // 許氏符號
        HsuText.Text = entry.HsuLabel;
        HsuText.Visibility = (!string.IsNullOrEmpty(entry.HsuLabel) && ShowHsu)
            ? Visibility.Visible : Visibility.Collapsed;

        // 寬鍵的字對齊
        if (entry.IsWideKey)
        {
            MainText.HorizontalAlignment = HorizontalAlignment.Left;
            MainText.Margin = new Thickness(6, 0, 0, 0);
            MainText.FontSize = 10;
        }
    }

    // ========== 高亮處理 ==========

    private static void OnHighlightedKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyButton btn) return;

        if (e.OldValue is ObservableCollection<string> oldCol)
            oldCol.CollectionChanged -= btn.OnHighlightCollectionChanged;

        if (e.NewValue is ObservableCollection<string> newCol)
            newCol.CollectionChanged += btn.OnHighlightCollectionChanged;

        btn.RefreshHighlight();
    }

    private void OnHighlightCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        if (_entry == null) return;
        bool isHighlighted = HighlightedKeys?.Contains(_entry.KeyId) == true;

        if (isHighlighted)
            StartHighlight();
        else
            ClearHighlight();
    }

    private void StartHighlight()
    {
        // 停止現有淡出動畫
        _fadeStoryboard?.Stop(KeyBorder);

        // 選擇高亮色
        var brush = _entry?.IsModifier == true ? ModifierHighlightBrush : HighlightBrush;
        KeyBorder.Background = brush;

        // 建立淡出動畫（從高亮色漸回普通色，500ms）
        var colorAnim = new ColorAnimation
        {
            From = brush.Color,
            To = _entry?.IsWideKey == true ? WideBrush.Color : NormalBrush.Color,
            Duration = new Duration(TimeSpan.FromMilliseconds(500)),
            BeginTime = TimeSpan.FromMilliseconds(100) // 短暫停留後才淡出
        };

        Storyboard.SetTarget(colorAnim, KeyBorder);
        Storyboard.SetTargetProperty(colorAnim,
            new PropertyPath("Background.Color"));

        _fadeStoryboard = new Storyboard();
        _fadeStoryboard.Children.Add(colorAnim);
        _fadeStoryboard.Begin(KeyBorder, HandoffBehavior.SnapshotAndReplace);
    }

    private void ClearHighlight()
    {
        _fadeStoryboard?.Stop(KeyBorder);
        _fadeStoryboard = null;
        KeyBorder.Background = _entry?.IsWideKey == true ? WideBrush : NormalBrush;
    }

    private static void OnShowHsuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyButton btn && btn._entry != null)
        {
            bool show = (bool)e.NewValue;
            btn.HsuText.Visibility = (!string.IsNullOrEmpty(btn._entry.HsuLabel) && show)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
