using System.Runtime.InteropServices;

namespace QuickReader;

/// <summary>The few Windows functions the app needs that .NET doesn't wrap.</summary>
internal static class NativeMethods
{
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_DPICHANGED = 0x02E0;
    public const int MA_NOACTIVATE = 3;

    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    private const int VK_C = 0x43;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>Presses Ctrl+C in whatever window has the keyboard focus.</summary>
    public static void SendCtrlC() => SendKeyStrokes(
        (VK_CONTROL, false), (VK_C, false), (VK_C, true), (VK_CONTROL, true));

    /// <summary>Lets go of keys that are still held down (e.g. Alt from a shortcut).</summary>
    public static void ReleaseKeys(IEnumerable<int> virtualKeys) =>
        SendKeyStrokes(virtualKeys.Select(vk => (vk, true)).ToArray());

    private static void SendKeyStrokes(params (int VirtualKey, bool KeyUp)[] keys)
    {
        var inputs = new INPUT[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i].type = INPUT_KEYBOARD;
            inputs[i].U.ki = new KEYBDINPUT
            {
                wVk = (ushort)keys[i].VirtualKey,
                wScan = (ushort)MapVirtualKey((uint)keys[i].VirtualKey, 0),
                dwFlags = keys[i].KeyUp ? KEYEVENTF_KEYUP : 0,
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Waits until Windows has drawn the screen again (e.g. after hiding a window).</summary>
    public static void WaitForScreenRefresh()
    {
        try { DwmFlush(); } catch { /* not important */ }
    }

    /// <summary>Rounded corners on Windows 11. Windows 10 ignores this.</summary>
    public static void SetRoundedCorners(IntPtr hwnd, bool small)
    {
        int preference = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)); }
        catch { /* older Windows */ }
    }

#pragma warning disable CS0649 // fields are filled in by Windows / by layout only
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
#pragma warning restore CS0649
}
