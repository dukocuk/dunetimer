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

    public TimeSpan Remaining => TotalDuration - (DateTime.Now - StartTime);
    public bool IsFinished => Remaining <= TimeSpan.Zero;
    public double Progress => TotalDuration.TotalSeconds == 0 ? 1.0
        : Math.Clamp(1.0 - Remaining.TotalSeconds / TotalDuration.TotalSeconds, 0.0, 1.0);
}
