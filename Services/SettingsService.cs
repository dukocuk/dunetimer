using System.IO;
using System.Text.Json;
using DuneTimer.Models;

namespace DuneTimer.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public SettingsService()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        _settings = Load();
    }

    public List<ScanRegion> GetScanRegions()
        => _settings.ScanRegions;

    public void SaveScanRegions(List<ScanRegion> regions)
    {
        _settings.ScanRegions = regions;
        Save();
    }

    public void AddScanRegion(ScanRegion region)
    {
        _settings.ScanRegions.Add(region);
        Save();
    }

    public void AddScanRegions(IEnumerable<ScanRegion> regions)
    {
        _settings.ScanRegions.AddRange(regions);
        Save();
    }

    public void ClearScanRegions()
    {
        _settings.ScanRegions.Clear();
        Save();
    }

    public bool AutoScanEnabled
    {
        get => _settings.AutoScanEnabled;
        set { _settings.AutoScanEnabled = value; Save(); }
    }

    private AppSettings Load()
    {
        // A fresh install has nothing to migrate away from — start at the
        // current version so the migration below doesn't wipe every restart's
        // freshly-saved regions (they'd otherwise deserialize back as
        // SettingsVersion 0 next launch and get cleared again, forever).
        if (!File.Exists(_settingsPath))
            return new AppSettings { SettingsVersion = 2 };
        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            foreach (var region in settings.ScanRegions)
            {
                if (string.IsNullOrEmpty(region.Id))
                    region.Id = Guid.NewGuid().ToString("N")[..8];
            }

            // One-time migrations. Each bump clears saved scan regions once,
            // since a region's shape/meaning changed underneath it and an old
            // one saved to disk would silently keep behaving the old way:
            //  1: pre-anchor-detection regions (old "any time-like line is a
            //     timer" behavior) have no AnchorKind.
            //  2: ExtractionTime regions built before they spanned the full
            //     screen width (needed to see the Deathstill/panel tab name)
            //     — the narrow old ones can't resolve a tab name and would
            //     keep falling back to "Extraction Time" instead of it.
            if (settings.SettingsVersion < 2)
            {
                settings.ScanRegions.Clear();
                settings.SettingsVersion = 2;
                _settings = settings;
                Save();
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}

public class AppSettings
{
    public List<ScanRegion> ScanRegions { get; set; } = new();
    public bool AutoScanEnabled { get; set; } = false;
    public int SettingsVersion { get; set; } = 0;
}
