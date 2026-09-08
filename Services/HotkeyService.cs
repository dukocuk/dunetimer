using System.Windows;
using System.Windows.Interop;
using DuneTimer.Helpers;
using DuneTimer.Models;

namespace DuneTimer.Services;

public class HotkeyService : IDisposable
{
    private IntPtr _hwnd;
    private HwndSource? _source;
    private string[] _registeredActions = Array.Empty<string>();

    public event Action? ToggleOverlayRequested;
    public event Action? NewTimerRequested;
    public event Action? SelectRegionRequested;
    public event Action? ToggleScannerRequested;
    public event Action? ToggleInteractiveRequested;
    public event Action? AutoDetectRequested;

    public List<string> Register(Window window, Dictionary<string, HotkeyBinding> bindings)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        return RegisterAll(bindings);
    }

    // Unregisters whatever is currently bound and registers the new bindings
    // in its place — lets Settings apply a rebind live, with no app restart.
    public List<string> Reregister(Dictionary<string, HotkeyBinding> bindings)
    {
        UnregisterAll();
        return RegisterAll(bindings);
    }

    // Returns the action names that failed to register (combo already claimed
    // by another app), so callers can surface a warning instead of only
    // logging to the console.
    private List<string> RegisterAll(Dictionary<string, HotkeyBinding> bindings)
    {
        var failed = new List<string>();
        _registeredActions = HotkeyActions.All;

        foreach (var action in HotkeyActions.All)
        {
            if (!bindings.TryGetValue(action, out var binding))
                continue;

            var id = HotkeyActions.HotkeyId(action);
            var modifiers = binding.Modifiers | NativeMethods.MOD_NOREPEAT;

            if (!NativeMethods.RegisterHotKey(_hwnd, id, modifiers, binding.Key))
            {
                failed.Add(action);
                Console.WriteLine($"Warning: Failed to register {binding.ToDisplayString()} for {HotkeyActions.DisplayName(action)}. It might be used by another app.");
            }
        }

        return failed;
    }

    private void UnregisterAll()
    {
        foreach (var action in _registeredActions)
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyActions.HotkeyId(action));
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
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
