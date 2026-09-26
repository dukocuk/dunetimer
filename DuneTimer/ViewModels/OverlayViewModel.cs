using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DuneTimer.Models;
using DuneTimer.Services;

namespace DuneTimer.ViewModels;

public class OverlayViewModel : ViewModelBase
{
    private readonly TimerService _timerService;
    private readonly ScreenScannerService _scanner;
    private readonly SoundService _soundService;
    private readonly DispatcherTimer _refreshTimer;

    private static readonly Brush ScanningBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53)); // Green
    private static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x45, 0x60)); // Red

    private string _scannerStatusText = "Scanner off";
    private Brush _scannerStatusColor = OffBrush;
    private bool _isInteractive;
    private int _doneCountAtSync;

    private static readonly Brush LockedHeaderBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x45, 0x60));
    private static readonly Brush InteractiveHeaderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));

    public ObservableCollection<TimerItemViewModel> ActiveTimers { get; } = [];

    public Visibility EmptyStateVisibility =>
        ActiveTimers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // The overlay is click-through by default (so it never blocks game
    // clicks) — Alt+X toggles this off so buttons/dragging work. Reflected
    // here so the header visibly tells the user which mode they're in.
    public bool IsInteractive
    {
        get => _isInteractive;
        set
        {
            if (SetProperty(ref _isInteractive, value))
            {
                OnPropertyChanged(nameof(HeaderHintText));
                OnPropertyChanged(nameof(HeaderBrush));
            }
        }
    }

    public string HeaderHintText => IsInteractive ? "Alt+X to lock (click-through)" : "Alt+X to click buttons";

    public Brush HeaderBrush => IsInteractive ? InteractiveHeaderBrush : LockedHeaderBrush;

    public string ScannerStatusText
    {
        get => _scannerStatusText;
        set => SetProperty(ref _scannerStatusText, value);
    }

    public Brush ScannerStatusColor
    {
        get => _scannerStatusColor;
        set => SetProperty(ref _scannerStatusColor, value);
    }

    public OverlayViewModel(TimerService timerService, ScreenScannerService scanner, SoundService soundService)
    {
        _timerService = timerService;
        _scanner = scanner;
        _soundService = soundService;

        // Sync timers when collection changes
        _timerService.ActiveTimers.CollectionChanged += (_, _) => SyncTimers();
        // Completion no longer removes the timer, so re-sort explicitly to move
        // it to the top. OnTick runs on the UI dispatcher — no marshaling needed.
        _timerService.TimerCompleted += _ => SyncTimers();
        _timerService.CurrentZoneChanged += _ =>
        {
            OnPropertyChanged(nameof(CurrentZoneText));
            OnPropertyChanged(nameof(CurrentZoneBackground));
            OnPropertyChanged(nameof(CurrentZoneForeground));
            OnPropertyChanged(nameof(CurrentZoneTooltip));
        };

        // Status bar only ever shows the on/off scanning state. Everything else
        // the scanner reports (OCR reads, auto-detect summaries, errors) goes
        // to the console instead — those can fire from a background thread
        // (OCR runs off the UI thread), but Console.WriteLine is thread-safe
        // so no dispatcher marshaling is needed for them.
        _scanner.ScanningStateChanged += isScanning =>
            RunOnUiThread(() => OnScanningStateChanged(isScanning));
        // 60 chars used to hide whether an anchor word like "EXTRACTION" was
        // even present in a pass's raw OCR output — raised to a safety cap
        // far above any real single-region scan instead of a display width.
        _scanner.LastScanTextChanged += text => Console.WriteLine($"[Scanner] OCR read: {TruncateText(text, 2000)}");
        _scanner.ScanInfo += info => Console.WriteLine($"[Scanner] {info}");
        _scanner.ScanError += error => Console.WriteLine($"[Scanner] {error}");

        // Refresh display every 100ms for smooth countdown
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _refreshTimer.Tick += (_, _) =>
        {
            foreach (var vm in ActiveTimers)
                vm.Refresh();
            // A scanned done timer can be revived without a collection change —
            // re-sort so it drops back out of the done group at the top.
            if (_timerService.ActiveTimers.Count(t => t.IsDone) != _doneCountAtSync)
                SyncTimers();
        };
        _refreshTimer.Start();

        AutoDetectCommand = new DuneTimer.Helpers.RelayCommand(async _ => await _scanner.AutoDetectRegionsAsync());
        MuteCommand = new DuneTimer.Helpers.RelayCommand(_ =>
        {
            _soundService.ToggleMute();
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(MuteGlyph));
            foreach (var vm in ActiveTimers)
                vm.RefreshMute();
        });
        SettingsCommand = new DuneTimer.Helpers.RelayCommand(_ => OpenSettingsRequested?.Invoke());
        ClearDoneCommand = new DuneTimer.Helpers.RelayCommand(_ => _timerService.ClearCompleted());
        ToggleZoneCommand = new DuneTimer.Helpers.RelayCommand(_ => _timerService.ToggleZone());
    }

    // The zone new timers get tagged with — flipped when the player travels.
    public string CurrentZoneText => ZoneInfo.ShortName(_timerService.CurrentZone);
    public Brush CurrentZoneBackground => TimerItemViewModel.ZoneBackgroundOf(_timerService.CurrentZone);
    public Brush CurrentZoneForeground => TimerItemViewModel.ZoneForegroundOf(_timerService.CurrentZone);
    public string CurrentZoneTooltip =>
        $"New timers are tagged {ZoneInfo.DisplayName(_timerService.CurrentZone)} — click or Alt+L to switch";
    public System.Windows.Input.ICommand ToggleZoneCommand { get; }

    public System.Windows.Input.ICommand ClearDoneCommand { get; }

    public Visibility ClearDoneVisibility =>
        ActiveTimers.Any(t => t.IsDone) ? Visibility.Visible : Visibility.Collapsed;

    public System.Windows.Input.ICommand AutoDetectCommand { get; }
    public System.Windows.Input.ICommand MuteCommand { get; }
    public System.Windows.Input.ICommand SettingsCommand { get; }

    // Set by App.xaml.cs after construction — OverlayViewModel doesn't hold
    // the Settings/Hotkey services directly, so opening the window is
    // delegated the same way ControlPanelViewModel.CloseWindow is.
    public Action? OpenSettingsRequested { get; set; }

    public bool IsMuted => _soundService.IsMuted;
    public string MuteGlyph => IsMuted ? "🔇" : "🔊";

    private void SetStatus(string text, Brush color)
    {
        ScannerStatusText = text;
        ScannerStatusColor = color;
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void SyncTimers()
    {
        ActiveTimers.Clear();
        // Done timers first (oldest completion on top), then running by time left.
        var ordered = _timerService.ActiveTimers
            .OrderByDescending(t => t.IsDone)
            .ThenBy(t => t.CompletedAt)
            .ThenBy(t => t.Remaining);
        foreach (var timer in ordered)
            ActiveTimers.Add(new TimerItemViewModel(timer, id => _timerService.RemoveTimer(id), _soundService));
        _doneCountAtSync = _timerService.ActiveTimers.Count(t => t.IsDone);
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ClearDoneVisibility));
    }

    private void OnScanningStateChanged(bool isScanning)
    {
        SetStatus(isScanning ? "Scanner active" : "Scanner off", isScanning ? ScanningBrush : OffBrush);
    }

    private static string TruncateText(string text, int maxLength)
    {
        var cleaned = text.Replace("\n", " ").Replace("\r", "").Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] + "…" : cleaned;
    }
}
