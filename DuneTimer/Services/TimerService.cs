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

    public CraftingTimer AddOrUpdateTimerForRegion(string regionId, string name, int remainingSeconds, string icon = "⏱️")
    {
        // Match on (region, name) together — not region alone — since one padded
        // region can legitimately hold several distinct timers (e.g. a multi-slot
        // crafting queue); matching by region only would let two different-named
        // timers from the same region collide and overwrite each other.
        var existing = _timers.FirstOrDefault(t => t.SourceRegionId == regionId &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        // Adoption fallback: a detached/manual timer with a matching name gets
        // re-linked instead of spawning a duplicate (e.g. after a region was
        // pruned/replaced and Auto-Detect later rediscovers the same widget).
        existing ??= _timers.FirstOrDefault(t => t.SourceRegionId is null &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.SourceRegionId = regionId;
            if (remainingSeconds > existing.TotalDuration.TotalSeconds)
            {
                // OCR reads more remaining time than the timer's own total —
                // only possible if a new craft cycle started in the same spot.
                existing.StartTime = DateTime.Now;
                existing.TotalDuration = TimeSpan.FromSeconds(remainingSeconds);
            }
            else
            {
                // Preserve TotalDuration/progress; only nudge StartTime so the
                // countdown stays aligned with what the game shows. Safe to do
                // every tick since it never snaps the progress bar backward.
                existing.StartTime = DateTime.Now - (existing.TotalDuration - TimeSpan.FromSeconds(remainingSeconds));
            }
            return existing;
        }

        var timer = AddTimer(name, remainingSeconds, icon);
        timer.SourceRegionId = regionId;
        return timer;
    }

    public CraftingTimer AddOrUpdateAnchoredTimer(string regionId, string name, int remainingSeconds, string icon = "⏱️")
    {
        // Match on (region, name), same as the generic path — NOT region
        // alone. The anchor region is a fixed spot on screen (the top nav
        // bar), but which station is open there changes as the player walks
        // between refineries/appliances, and each one should get its own
        // independent timer rather than the newly-viewed station clobbering
        // whichever one was being tracked before.
        var existing = _timers.FirstOrDefault(t => t.SourceRegionId == regionId &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        // Adoption fallback: a detached timer (e.g. Ctrl+Alt+R just replaced
        // its region, or a prior region was pruned) gets re-linked by name
        // instead of spawning a duplicate on the next Auto-Detect/tick.
        existing ??= _timers.FirstOrDefault(t => t.SourceRegionId is null &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.SourceRegionId = regionId;
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
