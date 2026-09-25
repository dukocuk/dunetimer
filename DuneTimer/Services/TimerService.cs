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

    // Below this, a re-read can't be told apart from the game's whole-second
    // display rounding — snapping to it would just make the overlay jitter.
    private static readonly TimeSpan ResyncDeadband = TimeSpan.FromSeconds(1.5);

    // observedAt is when the screen was captured, not when OCR finished —
    // the background pass takes 0.5-3s, and anchoring to "now" would make the
    // timer read short by that (varying) amount after every re-sync.
    public CraftingTimer AddOrUpdateAnchoredTimer(string regionId, string name, int remainingSeconds, DateTime observedAt, string icon = "⏱️")
    {
        var remaining = TimeSpan.FromSeconds(remainingSeconds);

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
            var predicted = PredictAt(existing, observedAt);

            if (remaining > existing.TotalDuration)
            {
                existing.TotalDuration = remaining;
                StartRateSegment(existing, remaining, observedAt);
            }
            else if ((predicted - remaining).Duration() > SegmentBreakThreshold)
            {
                // Too far off to be the same countdown ticking along (queue
                // item finished, OCR misread) — resync, restart measuring.
                StartRateSegment(existing, remaining, observedAt);
            }
            else
            {
                // A new rate re-bases on the current prediction, not the raw
                // reading, so the display stays continuous instead of
                // jittering to each whole-second read.
                if (UpdateRate(existing, remaining, observedAt))
                    SyncTo(existing, predicted, observedAt);
                if ((PredictAt(existing, observedAt) - remaining).Duration() >= ResyncDeadband)
                    SyncTo(existing, remaining, observedAt);
            }

            Console.WriteLine($"[Timer] {name}: read {remainingSeconds}s, predicted {predicted.TotalSeconds:F1}s, rate {existing.Rate:F3}");
            return existing;
        }

        var timer = AddTimer(name, remainingSeconds, icon);
        timer.Rate = _learnedRates.GetValueOrDefault(name, 1.0);
        timer.SourceRegionId = regionId;
        StartRateSegment(timer, remaining, observedAt);
        return timer;
    }

    // A reading this far from the prediction isn't the same countdown
    // ticking along, so it ends the current rate-measuring segment.
    private static readonly TimeSpan SegmentBreakThreshold = TimeSpan.FromSeconds(5);

    // The whole-second display gives ±1s per reading, so measuring over less
    // than this would be too noisy (≤5% error at 20s, shrinking after).
    private static readonly TimeSpan MinRateSpan = TimeSpan.FromSeconds(20);
    private const double MinRate = 0.8, MaxRate = 1.25;

    // Last accepted rate per station name, so a new timer for a station
    // that was measured before starts at the right speed. In-memory only.
    private readonly Dictionary<string, double> _learnedRates = new(StringComparer.OrdinalIgnoreCase);

    private static TimeSpan PredictAt(CraftingTimer t, DateTime at) =>
        t.TotalDuration - (at - t.StartTime) * t.Rate;

    private static void SyncTo(CraftingTimer t, TimeSpan remaining, DateTime at) =>
        t.StartTime = at - (t.TotalDuration - remaining) / t.Rate;

    private static void StartRateSegment(CraftingTimer t, TimeSpan remaining, DateTime at)
    {
        t.RateAnchorAt = at;
        t.RateAnchorRemaining = remaining.TotalSeconds;
        SyncTo(t, remaining, at);
    }

    // Measures game-seconds per real second across the current segment
    // (first reading → this one). Returns true if the timer's rate changed.
    private bool UpdateRate(CraftingTimer t, TimeSpan remaining, DateTime at)
    {
        if (t.RateAnchorAt is not { } anchorAt) return false;
        var elapsed = at - anchorAt;
        if (elapsed < MinRateSpan) return false;

        double measured = (t.RateAnchorRemaining - remaining.TotalSeconds) / elapsed.TotalSeconds;
        if (measured < MinRate || measured > MaxRate) return false;

        t.Rate = measured;
        _learnedRates[t.Name] = measured;
        return true;
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
