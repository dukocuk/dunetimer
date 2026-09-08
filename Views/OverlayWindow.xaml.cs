using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DuneTimer.Helpers;

namespace DuneTimer.Views;

public partial class OverlayWindow : Window
{
    // Click-through starts ON so the overlay never blocks game clicks by
    // default. There's no way to interact with it (buttons, dragging) while
    // this is true — Alt+X (wired in App.xaml.cs) toggles it off.
    public bool IsClickThrough { get; private set; } = true;

    public OverlayWindow()
    {
        InitializeComponent();

        // Position: top-right corner of primary screen
        Left = SystemParameters.PrimaryScreenWidth - Width - 30;
        Top = 60;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SetClickThrough(true);
    }

    public void SetClickThrough(bool enabled)
    {
        IsClickThrough = enabled;
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        if (enabled)
            style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
        else
            style &= ~NativeMethods.WS_EX_TRANSPARENT;

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    // Returns the new state.
    public bool ToggleClickThrough()
    {
        SetClickThrough(!IsClickThrough);
        return IsClickThrough;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Dragging only works when click-through is off (Alt+X) — otherwise
        // this mouse event never reaches the window at all.
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if called without mouse capture
        }
    }
}
