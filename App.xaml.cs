using System.Windows;
using System.Windows.Media;
using DuneTimer.Helpers;
using DuneTimer.Services;
using DuneTimer.ViewModels;
using DuneTimer.Views;

namespace DuneTimer;

public partial class App : Application
{
    private OverlayWindow? _overlay;
    private OverlayViewModel? _overlayVm;
    private TimerService? _timerService;
    private RecipeService? _recipeService;
    private SoundService? _soundService;
    private HotkeyService? _hotkeyService;
    private ScreenScannerService? _scanner;
    private SettingsService? _settingsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Must happen before anything else touches Console — this is a WinExe
        // (GUI-subsystem) process, which gets no console of its own, so every
        // Console.WriteLine below would otherwise silently go nowhere.
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        base.OnStartup(e);

        // Initialize services
        _timerService = new TimerService();
        _recipeService = new RecipeService();
        _hotkeyService = new HotkeyService();
        _settingsService = new SettingsService();
        _soundService = new SoundService(_settingsService);

        var textParser = new TextParserService(_recipeService);
        _scanner = new ScreenScannerService(textParser, _timerService, _settingsService);

        // Play alert on timer completion, unless that entry was muted
        _timerService.TimerCompleted += timer =>
        {
            if (!timer.IsMuted)
                _soundService!.PlayAlert();
        };

        // Create overlay
        _overlayVm = new OverlayViewModel(_timerService, _scanner, _soundService);
        _overlay = new OverlayWindow
        {
            DataContext = _overlayVm
        };

        // Let the scanner hide/show the overlay around a one-off Auto-Detect
        // pass, and query its live physical-pixel rect to exclude it from the
        // periodic Queue-region rescan — its own HUD can sit inside both capture areas.
        _scanner.IsOverlayVisible = () => _overlay.IsVisible;
        _scanner.HideOverlay = () => _overlay.Hide();
        _scanner.ShowOverlay = () => _overlay.Show();
        _scanner.GetOverlayBounds = GetOverlayPhysicalRect;

        // Register global hotkeys after window handle is available
        _overlay.SourceInitialized += (_, _) =>
        {
            _hotkeyService!.Register(_overlay);
            _hotkeyService.ToggleOverlayRequested += ToggleOverlay;
            _hotkeyService.NewTimerRequested += ShowControlPanel;
            _hotkeyService.SelectRegionRequested += ShowRegionSelector;
            _hotkeyService.ToggleScannerRequested += () => _scanner!.ToggleScanning();
            _hotkeyService.ToggleInteractiveRequested += ToggleOverlayInteractive;
            _hotkeyService.AutoDetectRequested += async () =>
            {
                // Unlike the button's RelayCommand, this fires off a raw
                // WndProc hotkey callback — an unhandled exception here would
                // become an unhandled dispatcher exception, not a caught one.
                try { await _scanner!.AutoDetectRegionsAsync(); }
                catch (Exception ex) { Console.WriteLine($"[AutoDetect] Alt+D error: {ex.Message}"); }
            };
        };

        _overlay.Show();

        // Console output for debugging
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine("  ⏱  DUNE TIMER is running!");
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine("  Alt+T  — Show/hide overlay");
        Console.WriteLine("  Alt+N  — Add new timer (manual)");
        Console.WriteLine("  Ctrl+Alt+R — Select OCR scan region");
        Console.WriteLine("  Alt+S  — Start/stop OCR scanner");
        Console.WriteLine("  Alt+D  — Run Auto-Detect (works even if you can't click the overlay in-game)");
        Console.WriteLine("  Alt+X  — Toggle click-through (for dragging/clicking the overlay)");
        Console.WriteLine("═══════════════════════════════════════");
    }

    private System.Drawing.Rectangle? GetOverlayPhysicalRect()
    {
        if (_overlay is null || !_overlay.IsVisible) return null;

        var source = PresentationSource.FromVisual(_overlay);
        if (source?.CompositionTarget is null) return null;

        var transform = source.CompositionTarget.TransformToDevice;
        var topLeft = transform.Transform(new Point(_overlay.Left, _overlay.Top));
        var bottomRight = transform.Transform(new Point(_overlay.Left + _overlay.ActualWidth, _overlay.Top + _overlay.ActualHeight));

        return new System.Drawing.Rectangle(
            (int)topLeft.X, (int)topLeft.Y,
            (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
    }

    private void ToggleOverlayInteractive()
    {
        bool nowInteractive = !_overlay!.ToggleClickThrough();
        _overlayVm!.IsInteractive = nowInteractive;
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible)
            _overlay.Hide();
        else
            _overlay.Show();
    }

    private void ShowControlPanel()
    {
        var vm = new ControlPanelViewModel(_timerService!, _recipeService!);
        var window = new ControlPanelWindow { DataContext = vm };
        vm.CloseWindow = () => window.Close();
        window.Show();
    }

    private void ShowRegionSelector()
    {
        // Temporarily hide overlay so it doesn't interfere with selection
        var wasVisible = _overlay!.IsVisible;
        if (wasVisible)
            _overlay.Hide();

        // Pause the scanner so no periodic scan tick can run concurrently with
        // the dialog or with the Clear/Add below — ShowDialog still pumps the
        // dispatcher, so a tick would otherwise fire while the user selects.
        bool wasScanning = _scanner!.IsScanning;
        if (wasScanning)
            _scanner.StopScanning();

        var selector = new RegionSelectorWindow();
        var result = selector.ShowDialog();

        if (result == true && selector.SelectedRegion is not null)
        {
            var oldIds = _settingsService!.GetScanRegions().Select(r => r.Id).ToList();
            _settingsService.ClearScanRegions();
            _settingsService.AddScanRegion(selector.SelectedRegion);
            _timerService!.DetachTimersForRegions(oldIds);
        }

        if (wasScanning)
            _scanner.StartScanning();

        if (wasVisible)
            _overlay.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _scanner?.Dispose();
        base.OnExit(e);
    }
}
