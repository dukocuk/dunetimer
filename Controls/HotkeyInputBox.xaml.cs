using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DuneTimer.Helpers;
using DuneTimer.Models;

namespace DuneTimer.Controls;

// A "press keys to bind" capture box used by the Settings window — click it,
// press a modifier+key combo, and it becomes the new binding. Requires at
// least one modifier since a bare key would otherwise be stolen system-wide
// once registered as a global hotkey.
public partial class HotkeyInputBox : UserControl
{
    private static readonly Brush FocusedBorder = new SolidColorBrush(Color.FromRgb(0xE9, 0x45, 0x60));
    private static readonly Brush DefaultBorder = new SolidColorBrush(Color.FromRgb(0x0F, 0x34, 0x60));

    public static readonly DependencyProperty BindingValueProperty =
        DependencyProperty.Register(nameof(BindingValue), typeof(HotkeyBinding), typeof(HotkeyInputBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindingValueChanged));

    public HotkeyBinding? BindingValue
    {
        get => (HotkeyBinding?)GetValue(BindingValueProperty);
        set => SetValue(BindingValueProperty, value);
    }

    public HotkeyInputBox()
    {
        InitializeComponent();
        Focusable = true;
        Root.MouseLeftButtonDown += (_, _) => Focus();
        PreviewKeyDown += OnPreviewKeyDown;
        GotFocus += (_, _) =>
        {
            Root.BorderBrush = FocusedBorder;
            DisplayText.Text = "Press keys…";
        };
        LostFocus += (_, _) =>
        {
            Root.BorderBrush = DefaultBorder;
            UpdateDisplay();
        };
        UpdateDisplay();
    }

    private static void OnBindingValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HotkeyInputBox)d).UpdateDisplay();

    private void UpdateDisplay() => DisplayText.Text = BindingValue?.ToDisplayString() ?? "Unbound";

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;

        if (modifiers == 0)
        {
            DisplayText.Text = "Need Alt/Ctrl/Shift";
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        BindingValue = new HotkeyBinding { Modifiers = modifiers, Key = vk };
    }
}
