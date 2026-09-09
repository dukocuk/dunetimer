using DuneTimer.Helpers;

namespace DuneTimer.Models;

public class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(NativeMethods.VirtualKeyToDisplayString(Key));
        return string.Join("+", parts);
    }
}

public static class HotkeyActions
{
    public const string ToggleOverlay = "ToggleOverlay";
    public const string NewTimer = "NewTimer";
    public const string SelectRegion = "SelectRegion";
    public const string ToggleScanner = "ToggleScanner";
    public const string ToggleInteractive = "ToggleInteractive";
    public const string AutoDetect = "AutoDetect";

    public static readonly string[] All =
    {
        ToggleOverlay, NewTimer, SelectRegion, ToggleScanner, ToggleInteractive, AutoDetect
    };

    public static string DisplayName(string action) => action switch
    {
        ToggleOverlay => "Show/hide overlay",
        NewTimer => "Add new timer",
        SelectRegion => "Select OCR scan region",
        ToggleScanner => "Start/stop OCR scanner",
        ToggleInteractive => "Toggle click-through",
        AutoDetect => "Run Auto-Detect",
        _ => action
    };

    public static Dictionary<string, HotkeyBinding> Defaults() => new()
    {
        [ToggleOverlay] = new HotkeyBinding { Modifiers = NativeMethods.MOD_ALT, Key = NativeMethods.VK_T },
        [NewTimer] = new HotkeyBinding { Modifiers = NativeMethods.MOD_ALT, Key = NativeMethods.VK_N },
        [SelectRegion] = new HotkeyBinding { Modifiers = NativeMethods.MOD_ALT | NativeMethods.MOD_CONTROL, Key = NativeMethods.VK_R },
        [ToggleScanner] = new HotkeyBinding { Modifiers = NativeMethods.MOD_ALT, Key = NativeMethods.VK_S },
        [ToggleInteractive] = new HotkeyBinding { Modifiers = NativeMethods.MOD_ALT, Key = NativeMethods.VK_X },
        [AutoDetect] = new HotkeyBinding { Modifiers = NativeMethods.MOD_ALT, Key = NativeMethods.VK_D },
    };

    public static int HotkeyId(string action) => action switch
    {
        ToggleOverlay => NativeMethods.HOTKEY_TOGGLE_OVERLAY,
        NewTimer => NativeMethods.HOTKEY_NEW_TIMER,
        SelectRegion => NativeMethods.HOTKEY_SELECT_REGION,
        ToggleScanner => NativeMethods.HOTKEY_TOGGLE_SCANNER,
        ToggleInteractive => NativeMethods.HOTKEY_TOGGLE_INTERACTIVE,
        AutoDetect => NativeMethods.HOTKEY_AUTO_DETECT,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
