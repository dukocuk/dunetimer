namespace DuneTimer.Models;

public class CraftingTimer
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "⏱️";
    public TimeSpan TotalDuration { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
    public int Quantity { get; set; } = 1;
    public string? SourceRegionId { get; set; }
    public bool IsMuted { get; set; } = false;

    // Game seconds per real second. The in-game countdown doesn't tick at
    // exactly 1.0x, so scanned timers learn it (TimerService) — otherwise a
    // timer left running after the panel closes drifts behind the game.
    public double Rate { get; set; } = 1.0;

    // First reading of the current unbroken run of scans, used to measure Rate.
    public DateTime? RateAnchorAt { get; set; }
    public double RateAnchorRemaining { get; set; }

    public TimeSpan Remaining => TotalDuration - (DateTime.Now - StartTime) * Rate;
    public bool IsFinished => Remaining <= TimeSpan.Zero;
    public double Progress => TotalDuration.TotalSeconds == 0 ? 1.0
        : Math.Clamp(1.0 - Remaining.TotalSeconds / TotalDuration.TotalSeconds, 0.0, 1.0);
}
