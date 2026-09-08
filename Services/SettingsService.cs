using System.IO;
using System.Text.Json;
using DuneTimer.Models;

namespace DuneTimer.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly object _regionsLock = new();
    private AppSettings _settings;

    public SettingsService()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        _settings = Load();
    }

    // Returns a defensive copy — the scanner enumerates this from a background
    // thread (OCR runs off the UI thread) while the UI thread can concurrently
    // Add/Clear/Save regions (e.g. saving a manually-selected region). Handing
    // out the live list would let one thread mutate it mid-enumeration on the
    // other, which List<T> does not tolerate.
    public List<ScanRegion> GetScanRegions()
    {
        lock (_regionsLock) return _settings.ScanRegions.ToList();
    }

    public void SaveScanRegions(List<ScanRegion> regions)
    {
        lock (_regionsLock)
        {
            _settings.ScanRegions = regions;
            Save();
        }
    }

    public void AddScanRegion(ScanRegion region)
    {
        lock (_regionsLock)
        {
            _settings.ScanRegions.Add(region);
            Save();
        }
    }

    public void AddScanRegions(IEnumerable<ScanRegion> regions)
    {
        lock (_regionsLock)
        {
            _settings.ScanRegions.AddRange(regions);
            Save();
        }
    }

    public void ClearScanRegions()
    {
        lock (_regionsLock)
        {
            _settings.ScanRegions.Clear();
            Save();
        }
    }

    public bool AutoScanEnabled
    {
        get => _settings.AutoScanEnabled;
        set { _settings.AutoScanEnabled = value; Save(); }
    }

    public bool IsMuted
    {
        get => _settings.IsMuted;
        set { _settings.IsMuted = value; Save(); }
    }

    public Dictionary<string, HotkeyBinding> HotkeyBindings
    {
        get => _settings.HotkeyBindings;
        set { _settings.HotkeyBindings = value; Save(); }
    }

    // Empty string/null means "use the bundled default" (Assets/alert.wav,
    // falling back to the system beep if that's missing too).
    public string? AlertSoundPath
    {
        get => _settings.AlertSoundPath;
        set { _settings.AlertSoundPath = value; Save(); }
    }

    private AppSettings Load()
    {
        // A fresh install has nothing to migrate away from — start at the
        // current version so the migration below doesn't wipe every restart's
        // freshly-saved regions (they'd otherwise deserialize back as
        // SettingsVersion 0 next launch and get cleared again, forever).
        if (!File.Exists(_settingsPath))
            return new AppSettings { SettingsVersion = 3, HotkeyBindings = HotkeyActions.Defaults() };
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

            // 3: hotkeys became user-configurable. Anyone upgrading from an
            // older settings.json has no HotkeyBindings on disk yet — seed
            // today's defaults so their existing Alt+T/Alt+N/etc muscle memory
            // keeps working until they intentionally rebind something.
            if (settings.HotkeyBindings.Count == 0)
            {
                settings.HotkeyBindings = HotkeyActions.Defaults();
                settings.SettingsVersion = 3;
                _settings = settings;
                Save();
            }

            return settings;
        }
        catch
        {
            return new AppSettings { SettingsVersion = 3, HotkeyBindings = HotkeyActions.Defaults() };
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
    public bool IsMuted { get; set; } = false;
    public int SettingsVersion { get; set; } = 0;
    public Dictionary<string, HotkeyBinding> HotkeyBindings { get; set; } = new();
    public string? AlertSoundPath { get; set; } = null;
}
