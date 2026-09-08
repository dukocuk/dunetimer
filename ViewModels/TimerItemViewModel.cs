using System.Windows.Media;
using DuneTimer.Models;

namespace DuneTimer.ViewModels;

public class TimerItemViewModel : ViewModelBase
{
    private readonly CraftingTimer _timer;
    private const double MaxProgressWidth = 276; // Overlay width (320) minus padding (44)

    public TimerItemViewModel(CraftingTimer timer)
    {
        _timer = timer;
    }

    public string Name => _timer.Name;
    public string Icon => _timer.Icon;
    public string TimerId => _timer.Id;

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
