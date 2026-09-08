using System.Runtime.InteropServices;

namespace DuneTimer.Helpers;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint VK_T = 0x54;
    public const uint VK_N = 0x4E;
    public const uint VK_R = 0x52;
    public const uint VK_S = 0x53;
    public const uint VK_X = 0x58;
    public const uint VK_D = 0x44;

    public const int HOTKEY_TOGGLE_OVERLAY = 9001;
    public const int HOTKEY_NEW_TIMER = 9002;
    public const int HOTKEY_SELECT_REGION = 9003;
    public const int HOTKEY_TOGGLE_SCANNER = 9004;
    public const int HOTKEY_TOGGLE_INTERACTIVE = 9005;
    public const int HOTKEY_AUTO_DETECT = 9006;

    public const int WM_HOTKEY = 0x0312;

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Lets a WinExe (GUI-subsystem) process route Console.WriteLine into the
    // PowerShell/cmd window it was launched from — WinExe processes get no
    // console of their own, so without this every Console.WriteLine call
    // silently writes to nothing. No-op (returns false) when launched from
    // Explorer/no console parent, which is fine — normal play is unaffected.
    public const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int dwProcessId);
}
