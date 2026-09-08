using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DuneTimer.Services;

namespace DuneTimer.ViewModels;

public class OverlayViewModel : ViewModelBase
{
    private readonly TimerService _timerService;
    private readonly ScreenScannerService _scanner;
    private readonly DispatcherTimer _refreshTimer;

    private string _scannerStatusText = "Scanner off — Alt+S to start";
    private Brush _scannerStatusColor = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    private bool _isInteractive;

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

    public OverlayViewModel(TimerService timerService, ScreenScannerService scanner)
    {
        _timerService = timerService;
        _scanner = scanner;

        // Sync timers when collection changes
        _timerService.ActiveTimers.CollectionChanged += (_, _) => SyncTimers();

        // Scanner state changes
        _scanner.ScanningStateChanged += OnScanningStateChanged;
        _scanner.LastScanTextChanged += text =>
            ScannerStatusText = $"Scanner active — found: {TruncateText(text, 30)}";
        _scanner.ScanError += error =>
        {
            ScannerStatusText = error;
            ScannerStatusColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)); // Yellow
        };

        // Refresh display every 100ms for smooth countdown
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _refreshTimer.Tick += (_, _) =>
        {
            foreach (var vm in ActiveTimers)
                vm.Refresh();
        };
        _refreshTimer.Start();

        AutoDetectCommand = new DuneTimer.Helpers.RelayCommand(async _ => await _scanner.AutoDetectRegionsAsync());
    }

    public System.Windows.Input.ICommand AutoDetectCommand { get; }

    public void UpdateScannerStatus(string message)
    {
        ScannerStatusText = message;
    }

    private void SyncTimers()
    {
        ActiveTimers.Clear();
        foreach (var timer in _timerService.ActiveTimers.OrderBy(t => t.Remaining))
            ActiveTimers.Add(new TimerItemViewModel(timer));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void OnScanningStateChanged(bool isScanning)
    {
        if (isScanning)
        {
            ScannerStatusText = "Scanner active — scanning...";
            ScannerStatusColor = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53)); // Green
        }
        else
        {
            ScannerStatusText = "Scanner off — Alt+S to start";
            ScannerStatusColor = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)); // Gray
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        var cleaned = text.Replace("\n", " ").Replace("\r", "").Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] + "…" : cleaned;
    }
}
