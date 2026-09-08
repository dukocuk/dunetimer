using System.Windows;
using System.Windows.Interop;
using DuneTimer.Helpers;

namespace DuneTimer.Services;

public class HotkeyService : IDisposable
{
    private IntPtr _hwnd;
    private HwndSource? _source;

    public event Action? ToggleOverlayRequested;
    public event Action? NewTimerRequested;
    public event Action? SelectRegionRequested;
    public event Action? ToggleScannerRequested;
    public event Action? ToggleInteractiveRequested;
    public event Action? AutoDetectRequested;

    public void Register(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        var modifiers = NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        
        if (!NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_TOGGLE_OVERLAY, modifiers, NativeMethods.VK_T))
            Console.WriteLine("Warning: Failed to register Alt+T. It might be used by another app.");
            
        if (!NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_NEW_TIMER, modifiers, NativeMethods.VK_N))
            Console.WriteLine("Warning: Failed to register Alt+N. It might be used by another app.");
            
        if (!NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_SELECT_REGION, modifiers | NativeMethods.MOD_CONTROL, NativeMethods.VK_R))
        {
            Console.WriteLine("Warning: Failed to register Ctrl+Alt+R.");
        }
            
        if (!NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_TOGGLE_SCANNER, modifiers, NativeMethods.VK_S))
            Console.WriteLine("Warning: Failed to register Alt+S. It might be used by another app.");

        if (!NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_TOGGLE_INTERACTIVE, modifiers, NativeMethods.VK_X))
            Console.WriteLine("Warning: Failed to register Alt+X. It might be used by another app.");

        if (!NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_AUTO_DETECT, modifiers, NativeMethods.VK_D))
            Console.WriteLine("Warning: Failed to register Alt+D. It might be used by another app.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case NativeMethods.HOTKEY_TOGGLE_OVERLAY:
                    ToggleOverlayRequested?.Invoke();
                    handled = true;
                    break;
                case NativeMethods.HOTKEY_NEW_TIMER:
                    NewTimerRequested?.Invoke();
                    handled = true;
                    break;
                case NativeMethods.HOTKEY_SELECT_REGION:
                    SelectRegionRequested?.Invoke();
                    handled = true;
                    break;
                case NativeMethods.HOTKEY_TOGGLE_SCANNER:
                    ToggleScannerRequested?.Invoke();
                    handled = true;
                    break;
                case NativeMethods.HOTKEY_TOGGLE_INTERACTIVE:
                    ToggleInteractiveRequested?.Invoke();
                    handled = true;
                    break;
                case NativeMethods.HOTKEY_AUTO_DETECT:
                    AutoDetectRequested?.Invoke();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_TOGGLE_OVERLAY);
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_NEW_TIMER);
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_SELECT_REGION);
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_TOGGLE_SCANNER);
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_TOGGLE_INTERACTIVE);
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_AUTO_DETECT);
        _source?.RemoveHook(WndProc);
    }
}
