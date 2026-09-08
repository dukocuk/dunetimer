using System.Text.Json.Serialization;

namespace DuneTimer.Models;

public class Recipe
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("duration")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "⏱️";

    public string FormattedDuration
    {
        get
        {
            if (DurationSeconds >= 3600)
            {
                int h = DurationSeconds / 3600;
                int m = (DurationSeconds % 3600) / 60;
                return $"{h}h {m}m";
            }
            if (DurationSeconds >= 60)
            {
                int m = DurationSeconds / 60;
                int s = DurationSeconds % 60;
                return s > 0 ? $"{m}m {s}s" : $"{m}m";
            }
            return $"{DurationSeconds}s";
        }
    }

    public string DisplayText => $"{Icon} {Name} ({FormattedDuration})";
}
