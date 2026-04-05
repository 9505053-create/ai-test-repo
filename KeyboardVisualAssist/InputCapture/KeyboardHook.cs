using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist.InputCapture;

/// <summary>
/// 封裝 Win32 WH_KEYBOARD_LL Low-Level Keyboard Hook
/// Hook callback 盡量輕量，事件交由上層 queue 處理
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    #region Win32 API

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // Virtual Key Codes for modifiers
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;  // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CAPITAL = 0x14; // CapsLock

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookCallback; // 防止 GC 回收 delegate

    public event EventHandler<KeyEventData>? KeyEvent;

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        _hookCallback = HookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            AppLogger.Error($"SetWindowsHookEx 失敗，錯誤碼: {err}");
            throw new InvalidOperationException($"無法安裝鍵盤 Hook，Win32 錯誤: {err}");
        }
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // nCode < 0 必須直接 CallNext，不處理
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Hook callback 內只做最輕量的事：擷取狀態並發送事件
                var keyData = new KeyEventData
                {
                    VirtualKey = (int)kbStruct.vkCode,
                    IsKeyDown = isKeyDown,
                    Timestamp = DateTime.Now,
                    Modifiers = SnapshotModifiers()
                };

                // 非同步發送，不阻塞 Hook callback
                KeyEvent?.Invoke(this, keyData);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>在 Hook callback 時瞬間擷取修飾鍵狀態</summary>
    private static ModifierState SnapshotModifiers() => new()
    {
        Shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0,
        Ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0,
        Alt = (GetKeyState(VK_MENU) & 0x8000) != 0,
        Win = (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0,
        CapsLock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0
    };

    public void Dispose() => Uninstall();
}
