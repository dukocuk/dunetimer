using System.Collections.ObjectModel;
using System.Windows.Threading;
using DuneTimer.Models;

namespace DuneTimer.Services;

public class TimerService
{
    private readonly DispatcherTimer _ticker;
    private readonly List<CraftingTimer> _timers = [];

    public ObservableCollection<CraftingTimer> ActiveTimers { get; } = [];
    public event Action<CraftingTimer>? TimerCompleted;

    public TimerService()
    {
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _ticker.Tick += OnTick;
        _ticker.Start();
    }

    public CraftingTimer AddTimer(string name, int durationSeconds, string icon = "⏱️", int quantity = 1)
    {
        var totalDuration = TimeSpan.FromSeconds(durationSeconds * quantity);
        var timer = new CraftingTimer
        {
            Name = quantity > 1 ? $"{name} (x{quantity})" : name,
            Icon = icon,
            TotalDuration = totalDuration,
            StartTime = DateTime.Now,
            Quantity = quantity
        };

        _timers.Add(timer);
        ActiveTimers.Add(timer);
        return timer;
    }

    public CraftingTimer AddOrUpdateAnchoredTimer(string regionId, string name, int remainingSeconds, string icon = "⏱️")
    {
        // One timer per station name — NOT per region. Each station that's
        // viewed gets its own independent timer (rather than a newly-viewed
        // station clobbering the previous one), but regions are full-width
        // strips from y=0 and every region resolves every anchor kind it sees,
        // so several regions can read the same panel on the same tick. Prefer
        // this region's own timer, then fall back to any same-named timer
        // (linked to another region or detached) instead of spawning a duplicate.
        var existing = _timers.FirstOrDefault(t => t.SourceRegionId == regionId &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        existing ??= _timers.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            // Keep the original link when another region also sees it — only
            // re-link a detached timer, so the link doesn't flip-flop per tick.
            existing.SourceRegionId ??= regionId;
            if (remainingSeconds > existing.TotalDuration.TotalSeconds)
            {
                existing.StartTime = DateTime.Now;
                existing.TotalDuration = TimeSpan.FromSeconds(remainingSeconds);
            }
            else
            {
                existing.StartTime = DateTime.Now - (existing.TotalDuration - TimeSpan.FromSeconds(remainingSeconds));
            }
            return existing;
        }

        var timer = AddTimer(name, remainingSeconds, icon);
        timer.SourceRegionId = regionId;
        return timer;
    }

    public void DetachTimersForRegions(IEnumerable<string> regionIds)
    {
        var ids = new HashSet<string>(regionIds);
        foreach (var t in _timers.Where(t => t.SourceRegionId is not null && ids.Contains(t.SourceRegionId!)))
            t.SourceRegionId = null;
    }

    public void RemoveTimer(string timerId)
    {
        var timer = _timers.FirstOrDefault(t => t.Id == timerId);
        if (timer is not null)
        {
            _timers.Remove(timer);
            ActiveTimers.Remove(timer);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var completed = _timers.Where(t => t.IsFinished).ToList();
        foreach (var timer in completed)
        {
            _timers.Remove(timer);
            ActiveTimers.Remove(timer);
            TimerCompleted?.Invoke(timer);
        }
    }
}
