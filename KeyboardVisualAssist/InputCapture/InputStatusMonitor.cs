using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.InputCapture;

/// <summary>
/// 監控輸入狀態：中/英、全形/半形、CapsLock、NumLock、輸入法名稱
/// 每 500ms 輪詢一次
/// </summary>
public class InputStatusMonitor : INotifyPropertyChanged, IDisposable
{
    #region Win32 API

    [DllImport("user32.dll")] static extern short GetKeyState(int nVirtKey);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")]  static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("imm32.dll")]  static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [DllImport("imm32.dll",  CharSet = CharSet.Unicode)]
    static extern int ImmGetDescription(IntPtr hKL, System.Text.StringBuilder lpszDescription, int uBufLen);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int VK_CAPITAL  = 0x14;
    private const int VK_NUMLOCK  = 0x90;
    private const uint WM_IME_CONTROL = 0x0283;
    private const int IMC_GETCONVERSIONMODE = 0x0001;
    private const int IME_CMODE_CHINESE   = 0x0001;
    private const int IME_CMODE_FULLSHAPE = 0x0008;

    #endregion

    private readonly DispatcherTimer _timer;

    private bool _capsLock;
    private bool _numLock;
    private bool _isChinese;
    private bool _isFullShape;
    private string _imeName = "";

    public bool CapsLock   { get => _capsLock;   private set { _capsLock   = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public bool NumLock    { get => _numLock;    private set { _numLock    = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public bool IsChinese  { get => _isChinese;  private set { _isChinese  = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public bool IsFullShape{ get => _isFullShape; private set { _isFullShape= value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public string ImeName  { get => _imeName;    private set { _imeName    = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }

    /// <summary>給底部條顯示的一行狀態文字</summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            parts.Add(IsChinese   ? "中" : "英");
            parts.Add(IsFullShape ? "全" : "半");
            if (CapsLock) parts.Add("⇪");
            if (NumLock)  parts.Add("Num");
            if (!string.IsNullOrEmpty(ImeName))
                parts.Add(ImeName);
            return string.Join("  ", parts);
        }
    }

    public InputStatusMonitor()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (s, e) => Refresh();
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
        AppLogger.Info("InputStatusMonitor 啟動");
    }

    public void Stop() => _timer.Stop();

    private void Refresh()
    {
        try
        {
            // CapsLock / NumLock（全域 toggle state）
            CapsLock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
            NumLock  = (GetKeyState(VK_NUMLOCK) & 0x0001) != 0;

            // 中/英、全半形（透過 IME API 詢問前景視窗）
            var hWnd = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                var imeWnd = ImmGetDefaultIMEWnd(hWnd);
                if (imeWnd != IntPtr.Zero)
                {
                    var convMode = (int)SendMessage(imeWnd, WM_IME_CONTROL,
                        new IntPtr(IMC_GETCONVERSIONMODE), IntPtr.Zero);
                    IsChinese   = (convMode & IME_CMODE_CHINESE)   != 0;
                    IsFullShape = (convMode & IME_CMODE_FULLSHAPE) != 0;
                }

                // 輸入法名稱
                uint tid = GetWindowThreadProcessId(hWnd, out _);
                var hkl = GetKeyboardLayout(tid);
                var sb = new System.Text.StringBuilder(256);
                ImmGetDescription(hkl, sb, sb.Capacity);
                var name = sb.ToString().Trim();
                // ImmGetDescription 回傳空白時，表示是純英文鍵盤
                ImeName = ShortenImeName(name);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("InputStatusMonitor.Refresh 失敗", ex);
        }
    }

    private static string ShortenImeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "英文";
        // 常見台灣輸入法名稱對照
        if (name.Contains("自然輸入法") || name.Contains("Atur") || name.Contains("ATUR")) return "自然輸入法";
        if (name.Contains("微軟注音") || name.Contains("Microsoft Bopomofo"))               return "微軟注音";
        if (name.Contains("新注音"))                                                          return "微軟新注音";
        if (name.Contains("倉頡"))                                                            return "倉頡";
        if (name.Contains("速成"))                                                            return "速成";
        if (name.Contains("注音"))                                                            return "注音";
        if (name.Contains("Zhuyin") || name.Contains("zhuyin"))                              return "注音";
        // 英文鍵盤：ImmGetDescription 回空或 "United States"
        if (name.Contains("United States") || name.Contains("English"))                     return "英文";
        if (name.Length > 10) return name[..10];
        return name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose() => _timer.Stop();
}
