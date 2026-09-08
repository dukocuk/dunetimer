using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Threading;
using DuneTimer.Helpers;
using DuneTimer.Models;
using Tesseract;

namespace DuneTimer.Services;

public class ScreenScannerService : IDisposable
{
    private const double OverlapMergeThreshold = 0.5;
    private const int ProbationMissLimit = 10; // ~20s at the 2s tick interval; only applies to a region that has NEVER matched
    private const string OcrEngineFailedMessage = "OCR Engine failed to load. Check tessdata.";

    private readonly TextParserService _parser;
    private readonly TimerService _timerService;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _scanTimer;
    private readonly object _ocrLock = new();
    private readonly Dictionary<string, (int Misses, bool EverMatched, string? LastName)> _regionState = new();
    private TesseractEngine? _engine;

    private bool _isAutoDetecting;
    private bool _isTicking;

    // Wired by App.xaml.cs so the scanner can hide/show the overlay around a
    // one-off Auto-Detect pass (its own HUD sits inside the capture area) and
    // query the overlay's live physical-pixel rect to exclude it from the
    // periodic Queue-region rescan (which can't hide/show without flicker).
    public Func<bool>? IsOverlayVisible { get; set; }
    public Action? HideOverlay { get; set; }
    public Action? ShowOverlay { get; set; }
    public Func<System.Drawing.Rectangle?>? GetOverlayBounds { get; set; }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value) return;
            _isScanning = value;
            ScanningStateChanged?.Invoke(value);
        }
    }

    public event Action<bool>? ScanningStateChanged;
    public event Action<string>? LastScanTextChanged;
    public event Action<string>? ScanInfo;
    public event Action<string>? ScanError;

    public ScreenScannerService(
        TextParserService parser,
        TimerService timerService,
        SettingsService settings)
    {
        _parser = parser;
        _timerService = timerService;
        _settings = settings;

        try
        {
            var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);

            // Whitelist temporarily disabled to ensure we capture all text first
            // _engine.SetVariable("tessedit_char_whitelist", "0123456789:hms ");
            // _engine.SetVariable("tessedit_char_blacklist", "!?@#$%^&*()_+={}[]|\\;\"'<>,./~`");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize Tesseract: {ex.Message}");
        }

        _scanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _scanTimer.Tick += ScanTick;
    }

    public void StartScanning()
    {
        var regions = _settings.GetScanRegions();
        if (regions.Count == 0)
        {
            ScanError?.Invoke("No scan regions configured. Click Auto-Detect or press Ctrl+Alt+R.");
            return;
        }

        if (_engine is null)
        {
            ScanError?.Invoke(OcrEngineFailedMessage);
            return;
        }

        IsScanning = true;
        _scanTimer.Start();
    }

    public void StopScanning()
    {
        _scanTimer.Stop();
        IsScanning = false;
    }

    public void ToggleScanning()
    {
        if (IsScanning) StopScanning();
        else StartScanning();
    }

    public async Task AutoDetectRegionsAsync()
    {
        if (_engine is null)
        {
            ScanError?.Invoke(OcrEngineFailedMessage);
            return;
        }

        if (_isAutoDetecting)
        {
            ScanInfo?.Invoke("Auto-detect already running...");
            return;
        }

        if (_isTicking)
        {
            ScanInfo?.Invoke("Scanner busy — try Auto-Detect again in a moment.");
            return;
        }

        // If every known anchor kind already has a tracked region, skip the
        // expensive full-screen OCR sweep entirely — the periodic per-region
        // rescan already keeps each one's name/time current (including tab
        // switches), so there's nothing a fresh detect pass would add.
        var trackedKinds = _settings.GetScanRegions()
            .Where(r => r.AnchorKind is not null)
            .Select(r => r.AnchorKind!)
            .ToHashSet();
        if (Enum.GetValues<AnchorKind>().All(k => trackedKinds.Contains(k.ToString())))
        {
            ScanInfo?.Invoke("Already tracking every known panel type — nothing new to detect.");
            if (!IsScanning) StartScanning();
            return;
        }

        _isAutoDetecting = true;
        bool wasScanning = IsScanning;
        _scanTimer.Stop(); // raw stop, not StopScanning() — avoids flipping IsScanning/UI status just for this pass

        // Captured before the try so `finally` knows whether to restore it
        // even if something below throws before the hide actually happens.
        bool overlayWasVisible = IsOverlayVisible?.Invoke() ?? false;

        // Set inside the try, but only invoked as a ScanInfo event after the
        // finally below finishes restarting the scanner — that restart fires
        // its own ScanningStateChanged status text, so invoking the summary
        // afterward guarantees it's the last word instead of getting
        // immediately stomped by "Scanner active — scanning...".
        string? summary = null;

        try
        {
            // The overlay's own HUD can sit inside the full-screen capture
            // (and specifically inside the wide Queue region), so hide it for
            // this one-off pass rather than risk OCR-ing our own rendered
            // timer rows. Kept inside the try so a throw here still restores
            // it via finally, instead of leaving the overlay stuck hidden.
            if (overlayWasVisible)
            {
                HideOverlay?.Invoke();
                await Task.Delay(30); // let the compositor finish removing it before we capture
            }

            var result = await Task.Run(DetectAndMerge);
            if (result.Error is not null)
            {
                ScanError?.Invoke(result.Error);
                return;
            }

            // Everything below runs back on the UI thread (post-await) — this is
            // deliberately the ONLY place that touches TimerService/_settings for
            // this pass; DetectAndMerge itself is pure background computation
            // that returns plain data and touches nothing shared/UI-bound.
            if (result.Accepted.Count > 0)
                _settings.AddScanRegions(result.Accepted);

            // Every region DetectAndMerge produces is anchor-based (Queue or
            // ExtractionTime) — it never creates generic/legacy regions — so
            // every timer here goes through the anchored (region, name) upsert.
            foreach (var (regionId, name, seconds, icon) in result.Timers)
            {
                _timerService.AddOrUpdateAnchoredTimer(regionId, name, seconds, icon);
                _regionState[regionId] = (Misses: 0, EverMatched: true, LastName: name);
            }

            summary = result.Found == 0 ? "No timers found on screen."
                : result.Accepted.Count == 0 ? $"Found {result.Found} timer(s) — all already tracked."
                : $"Found {result.Accepted.Count} new timer region(s), added {result.Timers.Count} timer(s).";
        }
        finally
        {
            if (overlayWasVisible) ShowOverlay?.Invoke();
            _isAutoDetecting = false;
            if (_settings.GetScanRegions().Count > 0)
            {
                if (wasScanning) _scanTimer.Start();
                else StartScanning();
            }
        }

        if (summary is not null) ScanInfo?.Invoke(summary);
    }

    private (int Found, List<ScanRegion> Accepted,
             List<(string RegionId, string Name, int Seconds, string Icon)> Timers,
             string? Error) DetectAndMerge()
    {
        try
        {
            int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            Console.WriteLine($"[AutoDetect] metrics={screenWidth}x{screenHeight} (compare against your monitor's actual native resolution — if smaller, display scaling is virtualizing this process and coordinates will be off)");
            var fullRegion = new ScanRegion(0, 0, screenWidth, screenHeight);

            int scale = 2; // Use 2x upscale for full screen to balance speed and accuracy
            using var bitmap = CaptureAndPreprocess(fullRegion, scale);
            if (bitmap is null)
                return (0, [], [], "Auto-detect error: screen capture failed.");

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            using var pix = Pix.LoadFromMemory(ms.ToArray());

            // No overlay exclusion needed here — AutoDetectRegionsAsync hides
            // the overlay for the duration of this whole pass.
            var allLines = ExtractLines(pix, PageSegMode.SparseText, scale, fullRegion.X, fullRegion.Y, null);

            int found = 0;
            var candidates = new List<(int X, int Y, int W, int H, ScanRegion Padded, string LineText, AnchorKind Kind)>();
            foreach (var line in allLines)
            {
                var kind = TextParserService.MatchAnchor(line.Text);
                if (kind is null) continue;
                found++;

                var padded = BuildAnchorRegion(line, kind.Value, screenWidth, screenHeight);
                candidates.Add((line.X, line.Y, line.W, line.H, padded, line.Text, kind.Value));
            }

            Console.WriteLine($"[AutoDetect] scanned {allLines.Count} lines, matched {found} anchor line(s)");
            foreach (var c in candidates)
                Console.WriteLine($"[AutoDetect]   match: \"{c.LineText}\" ({c.Kind})");

            // Safe to read the live list here: _isAutoDetecting/_isTicking mutually
            // exclude this pass and ScanTick from ever running at the same time,
            // so nothing else can mutate _settings.ScanRegions concurrently.
            var existingRegions = _settings.GetScanRegions();
            var accepted = new List<ScanRegion>();
            foreach (var c in candidates)
            {
                // Dedup on the RAW (unpadded) box, not the padded region — two
                // nearby but distinct countdowns must not collapse into one
                // region just because their padded boxes overlap.
                bool alreadyCovered = existingRegions.Any(r => r.OverlapRatio(c.X, c.Y, c.W, c.H) > OverlapMergeThreshold)
                    || accepted.Any(a => a.OverlapRatio(c.X, c.Y, c.W, c.H) > OverlapMergeThreshold);
                if (alreadyCovered) continue;
                accepted.Add(c.Padded);
            }

            var timers = new List<(string, string, int, string)>();
            foreach (var region in accepted)
                foreach (var (itemName, remainingSeconds, icon) in ScanRegionOnce(region, overlayRect: null))
                    timers.Add((region.Id, itemName, remainingSeconds, icon));

            return (found, accepted, timers, null);
        }
        catch (Exception ex)
        {
            return (0, [], [], $"Auto-detect error: {ex.Message}");
        }
    }

    // Both anchors need to see the panel's highlighted tab name (e.g.
    // "Medium Ore Refinery", "Deathstill"), which sits at the top of the
    // screen and can be anywhere across the top bar — so both regions span
    // the full width from Y:0 down through the anchor line. The resulting
    // cross-column risk (e.g. Deathstill's WATER CAPACITY figures sitting at
    // a similar height to EXTRACTION TIME) is handled in
    // TextParserService.ResolveAnchoredTimer's column-restricted time search,
    // not by narrowing this region.
    private static ScanRegion BuildAnchorRegion(
        (int X, int Y, int W, int H, string Text) anchor, AnchorKind kind, int screenWidth, int screenHeight)
    {
        // The value isn't on the anchor's own line — e.g. Deathstill's
        // "EXTRACTION"/"TIME" label sits 3-4 text-lines above its "46m 28s"
        // value. Pad well past the anchor line itself (scaled off the
        // anchor's own text height so it adapts to resolution/UI scale, with
        // a generous floor) so the value is actually inside this region —
        // otherwise ResolveAnchoredTimer never finds a time to read.
        int verticalPad = Math.Max(anchor.H * 10, 250);
        int bottom = Math.Min(screenHeight, anchor.Y + anchor.H + verticalPad);
        return new ScanRegion(0, 0, screenWidth, bottom) { AnchorKind = kind.ToString() };
    }

    // Runs the OCR iterator, converts each line's bounding box from
    // scaled-crop coordinates to absolute physical-screen coordinates, drops
    // any line overlapping excludeRect (the live overlay rect, when
    // supplied — null means "exclude nothing", not "exclude everything"),
    // and returns lines sorted top-to-bottom by Y (OCR iteration order isn't
    // reliable enough for the anchor/value windowed search).
    private List<(int X, int Y, int W, int H, string Text)> ExtractLines(
        Pix pix, PageSegMode mode, int scale, int offsetX, int offsetY, System.Drawing.Rectangle? excludeRect)
    {
        var lines = new List<(int X, int Y, int W, int H, string Text)>();
        lock (_ocrLock)
        {
            using var page = _engine!.Process(pix, mode);
            using var iter = page.GetIterator();

            iter.Begin();
            do
            {
                string text = iter.GetText(PageIteratorLevel.TextLine)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect)) continue;

                int x = offsetX + rect.X1 / scale;
                int y = offsetY + rect.Y1 / scale;
                int w = (rect.X2 - rect.X1) / scale;
                int h = (rect.Y2 - rect.Y1) / scale;

                if (excludeRect is System.Drawing.Rectangle er &&
                    er.IntersectsWith(new System.Drawing.Rectangle(x, y, w, h)))
                    continue;

                lines.Add((x, y, w, h, text));
            } while (iter.Next(PageIteratorLevel.TextLine));
        }
        return lines.OrderBy(l => l.Y).ToList();
    }

    // Pure, thread-safe: capture + OCR + parse a single region and return data
    // only. No calls into TimerService/_settings/events beyond the benign
    // LastScanTextChanged status string — safe to call from a background thread.
    // overlayRect applies to both anchor kinds now — both span the full
    // screen width up to the anchor line (see BuildAnchorRegion), so both can
    // plausibly contain the overlay. Pass the same value computed once by the
    // caller for the whole batch — never re-query it here (background thread).
    private List<(string Name, int Seconds, string Icon)> ScanRegionOnce(ScanRegion region, System.Drawing.Rectangle? overlayRect, string? previousName = null)
    {
        if (!region.IsValid) return [];

        // Both anchor regions span the full screen width and, on a 4K-class
        // display, can be several million pixels before any upscale at all —
        // 3x there would be a many-times-full-screen OCR pass every 2s tick.
        // Scale down as the region grows so periodic ticks stay well inside
        // the 2s interval; small crops (manual regions) keep 3x for accuracy.
        long pixels = (long)region.Width * region.Height;
        int scale = pixels > 3_000_000 ? 1 : pixels > 1_000_000 ? 2 : 3;
        using var bitmap = CaptureAndPreprocess(region, scale);
        if (bitmap is null) return [];

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        using var pix = Pix.LoadFromMemory(ms.ToArray());

        if (region.AnchorKind is "Queue" or "ExtractionTime")
        {
            var kind = region.AnchorKind == "Queue" ? AnchorKind.Queue : AnchorKind.ExtractionTime;

            // SparseText for both: each is a wide, scattered multi-column
            // strip (top tab bar plus the anchor's own panel), not a single
            // block of text.
            var lines = ExtractLines(pix, PageSegMode.SparseText, scale, region.X, region.Y, overlayRect);
            if (lines.Count == 0) return [];

            LastScanTextChanged?.Invoke(string.Join('\n', lines.Select(l => l.Text)));
            var resolved = _parser.ResolveAnchoredTimer(lines, kind, previousName);
            return resolved is { } r ? [(r.Name, r.Seconds, "⏱️")] : [];
        }

        // Legacy/manual region (no AnchorKind): unchanged generic behavior.
        string text;
        lock (_ocrLock)
        {
            // PSM 6 is for a single block of text
            using var page = _engine!.Process(pix, PageSegMode.SingleBlock);
            text = page.GetText()?.Trim() ?? "";
        }

        if (string.IsNullOrWhiteSpace(text)) return [];

        LastScanTextChanged?.Invoke(text);
        return _parser.ParseCraftingText(text);
    }

    private async void ScanTick(object? sender, EventArgs e)
    {
        if (_isTicking || _isAutoDetecting || _engine is null) return;
        _isTicking = true;
        try
        {
            var regions = _settings.GetScanRegions();
            if (regions.Count == 0) return;

            // Recomputed fresh every tick — never cache this, the user can
            // drag the overlay between ticks.
            var overlayRect = GetOverlayBounds?.Invoke();

            // Snapshot each region's last-known resolved name before handing
            // off to the background pass — a transient single-tick OCR miss
            // on the station name then reuses this instead of falling back
            // to the generic "Crafting Queue"/"Extraction Time" literal,
            // which would otherwise register as a brand-new distinct timer.
            var previousNames = regions.ToDictionary(r => r.Id, r => _regionState.GetValueOrDefault(r.Id, (Misses: 0, EverMatched: false, LastName: null)).LastName);
            var results = await Task.Run(() => regions.Select(r => (Region: r, Parsed: ScanRegionOnce(r, overlayRect, previousNames[r.Id]))).ToList());

            var dead = new List<ScanRegion>();
            foreach (var (region, parsed) in results)
            {
                var state = _regionState.GetValueOrDefault(region.Id, (Misses: 0, EverMatched: false, LastName: null));
                if (parsed.Count == 0)
                {
                    state.Misses++;
                    if (!state.EverMatched && state.Misses >= ProbationMissLimit)
                    {
                        dead.Add(region); // only prune regions that NEVER once matched
                        _regionState.Remove(region.Id);
                        continue;
                    }
                    _regionState[region.Id] = state;
                    continue;
                }
                foreach (var (itemName, remainingSeconds, icon) in parsed)
                {
                    _regionState[region.Id] = (Misses: 0, EverMatched: true, LastName: itemName);
                    if (region.AnchorKind is not null)
                        _timerService.AddOrUpdateAnchoredTimer(region.Id, itemName, remainingSeconds, icon);
                    else
                        _timerService.AddOrUpdateTimerForRegion(region.Id, itemName, remainingSeconds, icon);
                }
            }

            if (dead.Count > 0)
            {
                var remaining = regions.Except(dead).ToList();
                _timerService.DetachTimersForRegions(dead.Select(d => d.Id));
                _settings.SaveScanRegions(remaining); // only writes to disk when a region is actually pruned
            }
        }
        catch (Exception ex)
        {
            ScanError?.Invoke($"Scan error: {ex.Message}");
        }
        finally
        {
            _isTicking = false;
        }
    }

    private static Bitmap? CaptureAndPreprocess(ScanRegion region, int scale = 3)
    {
        try
        {
            var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(region.X, region.Y, 0, 0, new Size(region.Width, region.Height));
            }

            var upscaled = new Bitmap(region.Width * scale, region.Height * scale, PixelFormat.Format32bppArgb);
            using (var g2 = Graphics.FromImage(upscaled))
            {
                if (scale > 1)
                    g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g2.DrawImage(bmp, 0, 0, upscaled.Width, upscaled.Height);
            }
            bmp.Dispose();

            // Convert to Grayscale to improve contrast for Tesseract
            var grayscale = new Bitmap(upscaled.Width, upscaled.Height, PixelFormat.Format32bppArgb);
            using (var g3 = Graphics.FromImage(grayscale))
            {
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] { .3f, .3f, .3f, 0, 0 },
                    new float[] { .59f, .59f, .59f, 0, 0 },
                    new float[] { .11f, .11f, .11f, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                });
                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g3.DrawImage(upscaled, new Rectangle(0, 0, upscaled.Width, upscaled.Height),
                    0, 0, upscaled.Width, upscaled.Height, GraphicsUnit.Pixel, attributes);
            }
            upscaled.Dispose();

#if DEBUG
            try {
                grayscale.Save(Path.Combine(AppContext.BaseDirectory, "debug_capture.png"), System.Drawing.Imaging.ImageFormat.Png);
            } catch { }
#endif

            return grayscale;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _engine?.Dispose();
    }
}
