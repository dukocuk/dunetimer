namespace DuneTimer.Models;

// Which map a timer's station is on. OCR can't tell them apart (the panels
// look identical), so the user flips a "current zone" when they travel.
public enum Zone
{
    HaggaBasin,
    DeepDesert
}

public static class ZoneInfo
{
    public static string ShortName(Zone zone) => zone == Zone.DeepDesert ? "DD" : "HB";
    public static string DisplayName(Zone zone) => zone == Zone.DeepDesert ? "Deep Desert" : "Hagga Basin";
    public static Zone Other(Zone zone) => zone == Zone.DeepDesert ? Zone.HaggaBasin : Zone.DeepDesert;
}
