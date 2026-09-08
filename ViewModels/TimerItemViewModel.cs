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
    }

    public string Name => _timer.Name;
    public string Icon => _timer.Icon;
    public string TimerId => _timer.Id;

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
            if (remaining <= TimeSpan.Zero) return "DONE!";
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }

    public double ProgressWidth => _timer.Progress * MaxProgressWidth;

    public Brush TimerColor => _timer.Remaining.TotalSeconds < 60
        ? new SolidColorBrush(Color.FromRgb(0xE9, 0x45, 0x60))  // Accent red
        : new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)); // White

    public void Refresh()
    {
        OnPropertyChanged(nameof(FormattedRemaining));
        OnPropertyChanged(nameof(ProgressWidth));
        OnPropertyChanged(nameof(TimerColor));
    }
}
