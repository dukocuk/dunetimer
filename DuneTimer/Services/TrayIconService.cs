using System.Drawing;
using System.Windows.Forms;

namespace DuneTimer.Services;

// Wraps a System.Windows.Forms.NotifyIcon — WPF has no built-in tray icon API.
// Mirrors HotkeyService's shape: construct, wire up *Requested events, Dispose.
public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleOverlayItem;

    // Queried when the context menu is about to open, so the "Show/Hide
    // Overlay" label reflects current state without the caller having to
    // push updates in every place overlay visibility changes.
    public Func<bool>? IsOverlayVisible { get; set; }

    public event Action? ToggleOverlayRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _toggleOverlayItem = new ToolStripMenuItem("Show/Hide Overlay", null, (_, _) => ToggleOverlayRequested?.Invoke());
        var settingsItem = new ToolStripMenuItem("Settings", null, (_, _) => OpenSettingsRequested?.Invoke());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleOverlayItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            var visible = IsOverlayVisible?.Invoke() ?? true;
            _toggleOverlayItem.Text = visible ? "Hide Overlay" : "Show Overlay";
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DuneTimer",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleOverlayRequested?.Invoke();
    }

    // No .ico asset ships with the app — reuse whatever icon the exe already
    // has (upgrades automatically if ApplicationIcon is ever set in the
    // csproj), falling back to a generic system icon if that fails.
    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is not null)
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
            // Fall through to the system icon below.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        // Visible must be cleared before Dispose, or the icon can linger in
        // the tray until the user mouses over it after the process exits.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
