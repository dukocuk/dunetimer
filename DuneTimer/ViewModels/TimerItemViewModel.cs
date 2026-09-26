using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DuneTimer.Helpers;
using DuneTimer.Models;
using DuneTimer.Services;

namespace DuneTimer.ViewModels;

public class TimerItemViewModel : ViewModelBase
{
    private readonly CraftingTimer _timer;
    private readonly SoundService _soundService;
    private const double MaxProgressWidth = 276; // Overlay width (320) minus padding (44)

    public TimerItemViewModel(CraftingTimer timer, Action<string> onDelete, SoundService soundService)
    {
        _timer = timer;
        _soundService = soundService;
        DeleteCommand = new RelayCommand(_ => onDelete(_timer.Id));
        ToggleMuteCommand = new RelayCommand(_ =>
        {
            _timer.IsMuted = !_timer.IsMuted;
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(MuteGlyph));
        });
        // A correction for a mis-tagged card. A scanned timer moved to the other
        // zone stops matching scans in this one — which is the point.
        ToggleZoneCommand = new RelayCommand(_ =>
        {
            _timer.Zone = ZoneInfo.Other(_timer.Zone);
            OnPropertyChanged(nameof(ZoneText));
            OnPropertyChanged(nameof(ZoneBackground));
            OnPropertyChanged(nameof(ZoneForeground));
            OnPropertyChanged(nameof(ZoneTooltip));
        });
    }

    public string Name => _timer.Name;
    public string Icon => _timer.Icon;
    public string TimerId => _timer.Id;

    public string ZoneText => ZoneInfo.ShortName(_timer.Zone);
    public Brush ZoneBackground => ZoneBackgroundOf(_timer.Zone);
    public Brush ZoneForeground => ZoneForegroundOf(_timer.Zone);
    public string ZoneTooltip => $"{ZoneInfo.DisplayName(_timer.Zone)} — click to switch";
    public ICommand ToggleZoneCommand { get; }

    // Sand for Hagga Basin, blue-violet for the Deep Desert. The HB/DD text
    // carries the meaning too, so the colors are never the only cue.
    private static readonly Brush HaggaBg = Frozen(0xC8, 0x92, 0x3A);
    private static readonly Brush HaggaFg = Frozen(0x1A, 0x1A, 0x2E);
    private static readonly Brush DeepBg = Frozen(0x4E, 0x6C, 0xF0);
    private static readonly Brush DeepFg = Frozen(0xFF, 0xFF, 0xFF);

    internal static Brush ZoneBackgroundOf(Zone zone) => zone == Zone.DeepDesert ? DeepBg : HaggaBg;
    internal static Brush ZoneForegroundOf(Zone zone) => zone == Zone.DeepDesert ? DeepFg : HaggaFg;

    // Global "Mute All" overlays every row's own preference without overwriting
    // it, so turning Mute All back off reveals each timer's prior individual state.
    public bool IsMuted => _timer.IsMuted || _soundService.IsMuted;
    public string MuteGlyph => IsMuted ? "🔇" : "🔊";
    public bool CanToggleMute => !_soundService.IsMuted;

    public ICommand DeleteCommand { get; }
    public ICommand ToggleMuteCommand { get; }

    public void RefreshMute()
    {
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(MuteGlyph));
        OnPropertyChanged(nameof(CanToggleMute));
    }

    public string FormattedRemaining
    {
        get
        {
            var remaining = _timer.Remaining;
            if (remaining <= TimeSpan.Zero) return "00:00";
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }

    public double ProgressWidth => _timer.Progress * MaxProgressWidth;

    private static readonly Brush AccentRed = Frozen(0xE9, 0x45, 0x60);
    private static readonly Brush White = Frozen(0xEA, 0xEA, 0xEA);
    private static readonly Brush DoneGreen = Frozen(0x00, 0xC8, 0x53);
    private static readonly Brush FlashBg = Frozen(0x1E, 0x7A, 0x45);
    private static readonly Brush DoneBg = Frozen(0x15, 0x30, 0x2B);
    private static readonly Brush CardBg = Frozen(0x16, 0x21, 0x3E);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // Blink briefly on completion, then settle into a steady done state —
    // constant flashing gets tuned out. Half-period 450ms keeps it well under
    // the 3-flashes-per-second accessibility limit. Derived from CompletedAt
    // rather than held here, since OverlayViewModel rebuilds item VMs often.
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(3);
    private const double FlashHalfPeriodMs = 450;

    public bool IsDone => _timer.IsDone;

    // A done card collapses to one quiet line (✓ name · ago · ✕): the big
    // countdown styling stays reserved for timers that still need watching.
    public Visibility RunningVisibility => _timer.IsDone ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DoneVisibility => _timer.IsDone ? Visibility.Visible : Visibility.Collapsed;

    public bool IsFlashing
    {
        get
        {
            if (_timer.CompletedAt is not { } at) return false;
            var elapsed = DateTime.Now - at;
            return elapsed < FlashDuration && (int)(elapsed.TotalMilliseconds / FlashHalfPeriodMs) % 2 == 0;
        }
    }

    public string DoneAgoText
    {
        get
        {
            if (_timer.CompletedAt is not { } at) return "";
            var ago = DateTime.Now - at;
            if (ago.TotalMinutes < 1) return "just now";
            if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes}m ago";
            return $"{(int)ago.TotalHours}h {ago.Minutes}m ago";
        }
    }

    public Brush TimerColor => _timer.Remaining.TotalSeconds < 60 ? AccentRed : White;

    public Brush CardBackground => IsFlashing ? FlashBg : _timer.IsDone ? DoneBg : CardBg;
    public Brush CardBorderBrush => _timer.IsDone ? DoneGreen : Brushes.Transparent;

    public void Refresh()
    {
        OnPropertyChanged(nameof(FormattedRemaining));
        OnPropertyChanged(nameof(ProgressWidth));
        OnPropertyChanged(nameof(TimerColor));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(DoneVisibility));
        OnPropertyChanged(nameof(DoneAgoText));
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
    }
}
