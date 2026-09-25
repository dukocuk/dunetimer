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
    private const int AutoDetectMaxAttempts = 5; // a single live-game frame can OCR badly; retry a few times before giving up
    // 200ms used to be inside a single UI animation/particle-FX state, so
    // fast retries kept re-sampling the same bad frame instead of a
    // different one (confirmed live: a whole 3x200ms burst missed Deathstill's
    // EXTRACTION anchor together, while separate manual re-presses each hit
    // on their first attempt). ~700ms is long enough to likely land on a
    // different frame; worst case for one Alt+D press is now ~3.5s instead of
    // ~0.6s, an accepted tradeoff since this only runs on a user-triggered,
    // infrequent pass, never on the periodic per-tick scan.
    private const int AutoDetectRetryDelayMs = 700;
    private const string OcrEngineFailedMessage = "OCR Engine failed to load. Check tessdata.";

    // Unverified against the live game — confirm next time it's running (Task
    // Manager > Details tab for the exact process name; Alt-Tab for the exact
    // window title) and correct here if scanning never resumes while in-game.
    private const string GameWindowTitleFragment = "Dune";
    private const string GameProcessNameFragment = "dunesandbox";

    private readonly TextParserService _parser;
    private readonly TimerService _timerService;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _scanTimer;
    private readonly object _ocrLock = new();
    private readonly Dictionary<string, (int Misses, bool EverMatched, string? LastName)> _regionState = new();
    private TesseractEngine? _engine;

    private bool _isAutoDetecting;
    private bool _isTicking;
    private bool _lastGameFocusState = true;

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
            // TesseractEngine's native-library loader locates leptonica/tesseract50.dll via
            // Assembly.GetExecutingAssembly().Location, which is empty under single-file publish
            // (the managed assembly is bundled, not a loose file) — CustomSearchPath overrides that.
            TesseractEnviornment.CustomSearchPath = AppContext.BaseDirectory;
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

    // Starting the scanner also runs one Auto-Detect pass, so the user doesn't
    // have to press Alt+D first. Scanning is switched on BEFORE the pass so
    // AutoDetectRegionsAsync sees wasScanning == true and restores the timer
    // itself when it finishes.
    public void StartScanning()
    {
        if (!StartScanningCore()) return;
        _ = RunStartupAutoDetectAsync();
    }

    private async Task RunStartupAutoDetectAsync()
    {
        try { await AutoDetectRegionsAsync(); }
        catch (Exception ex) { ScanError?.Invoke($"Auto-detect error: {ex.Message}"); }
    }

    // Just the tick-loop start, with no detect pass. Zero regions is fine here:
    // ScanTick returns quietly on an empty list, and Auto-Detect fills it in.
    private bool StartScanningCore()
    {
        if (_engine is null)
        {
            ScanError?.Invoke(OcrEngineFailedMessage);
            return false;
        }

        IsScanning = true;
        _scanTimer.Start();
        return true;
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

        if (!IsGameForeground())
        {
            ScanInfo?.Invoke("Dune: Awakening isn't the focused window — Auto-Detect needs the game on screen.");
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
            foreach (var (regionId, name, seconds, icon, capturedAt) in result.Timers)
            {
                _timerService.AddOrUpdateAnchoredTimer(regionId, name, seconds, capturedAt, icon);
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
            // wasScanning restarts unconditionally: Start now begins scanning
            // with zero regions, and IsScanning would otherwise stay true
            // with a dead timer if this pass found nothing.
            if (wasScanning) _scanTimer.Start();
            else if (_settings.GetScanRegions().Count > 0)
                StartScanningCore(); // not StartScanning() — that would kick off another detect pass
        }

        if (summary is not null) ScanInfo?.Invoke(summary);
    }

    private (int Found, List<ScanRegion> Accepted,
             List<(string RegionId, string Name, int Seconds, string Icon, DateTime CapturedAt)> Timers,
             string? Error) DetectAndMerge()
    {
        try
        {
            int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            Console.WriteLine($"[AutoDetect] metrics={screenWidth}x{screenHeight} (compare against your monitor's actual native resolution — if smaller, display scaling is virtualizing this process and coordinates will be off)");
            var fullRegion = new ScanRegion(0, 0, screenWidth, screenHeight);

            int found = 0;
            var candidates = new List<(int X, int Y, int W, int H, ScanRegion Padded, string LineText, AnchorKind Kind)>();

            // A single live-game frame can OCR badly (HUD animation, particle
            // FX, transient UI motion mid-render) and miss the anchor word
            // entirely even though the panel is clearly on screen — retry a
            // few times on a fresh capture before reporting nothing found,
            // instead of making the user re-press Alt+D themselves.
            for (int attempt = 1; attempt <= AutoDetectMaxAttempts; attempt++)
            {
                // 2x balances speed/accuracy for most attempts, but if
                // everything so far has come up empty, spend the last couple
                // of attempts at 3x instead of sampling another noisy 2x
                // frame — confirmed live that a whole burst of same-scale
                // retries can miss an anchor together while a cleaner frame
                // reads it fine. Full-screen 3x roughly doubles the 2x cost,
                // so this only fires as a last resort, never every attempt.
                int scale = found == 0 && attempt > AutoDetectMaxAttempts - 2 ? 3 : 2;

                using var bitmap = CaptureAndPreprocess(fullRegion, scale, out _);
                if (bitmap is null)
                    return (0, [], [], "Auto-detect error: screen capture failed.");

                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                using var pix = Pix.LoadFromMemory(ms.ToArray());

                // No overlay exclusion needed here — AutoDetectRegionsAsync hides
                // the overlay for the duration of this whole pass.
                var allLines = ExtractLines(pix, PageSegMode.SparseText, scale, fullRegion.X, fullRegion.Y, null);

                found = 0;
                candidates.Clear();
                foreach (var line in allLines)
                {
                    var kind = TextParserService.MatchAnchor(line.Text);
                    if (kind is null) continue;
                    found++;

                    var padded = BuildAnchorRegion(line, kind.Value, screenWidth, screenHeight);
                    candidates.Add((line.X, line.Y, line.W, line.H, padded, line.Text, kind.Value));
                }

                Console.WriteLine($"[AutoDetect] attempt {attempt}/{AutoDetectMaxAttempts} (scale={scale}): scanned {allLines.Count} lines, matched {found} anchor line(s)");
                foreach (var c in candidates)
                    Console.WriteLine($"[AutoDetect]   match: \"{c.LineText}\" ({c.Kind})");

                if (found > 0 || attempt == AutoDetectMaxAttempts) break;
                Thread.Sleep(AutoDetectRetryDelayMs);
            }

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
                // Same-kind only: anchor regions are full-width strips from
                // y=0, so a taller region of another kind (e.g. Deathstill's
                // Extraction) would otherwise swallow a Queue anchor and it
                // would never be tracked.
                string kindName = c.Kind.ToString();
                bool alreadyCovered = existingRegions.Any(r => r.AnchorKind == kindName && r.OverlapRatio(c.X, c.Y, c.W, c.H) > OverlapMergeThreshold)
                    || accepted.Any(a => a.AnchorKind == kindName && a.OverlapRatio(c.X, c.Y, c.W, c.H) > OverlapMergeThreshold);
                if (alreadyCovered) continue;
                accepted.Add(c.Padded);
            }

            var timers = new List<(string, string, int, string, DateTime)>();
            foreach (var region in accepted)
                foreach (var (itemName, remainingSeconds, icon, capturedAt) in ScanRegionOnce(region, overlayRect: null))
                    timers.Add((region.Id, itemName, remainingSeconds, icon, capturedAt));

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
        int verticalPad = TextParserService.VerticalPad(anchor.H);
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
    private List<(string Name, int Seconds, string Icon, DateTime CapturedAt)> ScanRegionOnce(ScanRegion region, System.Drawing.Rectangle? overlayRect, string? previousName = null)
    {
        if (!region.IsValid) return [];

        // Both anchor regions span the full screen width and, on a 4K-class
        // display, can be several million pixels before any upscale at all —
        // 3x there would be a many-times-full-screen OCR pass every 2s tick.
        // Scale down as the region grows so periodic ticks stay well inside
        // the 2s interval; small crops (manual regions) keep 3x for accuracy.
        long pixels = (long)region.Width * region.Height;
        int scale = pixels > 3_000_000 ? 1 : pixels > 1_000_000 ? 2 : 3;
        using var bitmap = CaptureAndPreprocess(region, scale, out var capturedAt);
        if (bitmap is null) return [];

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        using var pix = Pix.LoadFromMemory(ms.ToArray());

        // Always set from kind.ToString() in BuildAnchorRegion, so this is a
        // safe exact round-trip — not a ternary, now that there are 3 kinds.
        var kind = Enum.Parse<AnchorKind>(region.AnchorKind!);

        // SparseText for all three: each is a wide, scattered multi-column
        // strip (top tab bar plus the anchor's own panel), not a single
        // block of text.
        var lines = ExtractLines(pix, PageSegMode.SparseText, scale, region.X, region.Y, overlayRect);
        if (lines.Count == 0) return [];

        LastScanTextChanged?.Invoke(string.Join('\n', lines.Select(l => l.Text)));

        // Resolve every anchor kind visible in this read, not just the one
        // the region was created for. Regions are full-width strips from y=0,
        // so walking from e.g. a Deathstill (Extraction) to a refinery (Queue)
        // shows the new panel inside the existing region — picking it up here
        // tracks it on the next tick instead of waiting for a manual Alt+D.
        // The region's own kind goes first and is always tried, so miss
        // counting and the "no time" hint behave as before.
        var kinds = lines
            .Select(l => TextParserService.MatchAnchor(l.Text))
            .OfType<AnchorKind>()
            .Where(k => k != kind)
            .Distinct()
            .Prepend(kind);

        var results = new List<(string Name, int Seconds, string Icon, DateTime CapturedAt)>();
        foreach (var k in kinds)
        {
            // previousName belongs to the region's own kind only — reusing it
            // for another kind could label a refinery with a Deathstill name
            // on a tick where the station tab failed to OCR.
            string? prev = k == kind ? previousName : null;
            if (ResolveKind(lines, k, scale, overlayRect, prev, capturedAt) is { } r)
                results.Add((r.Name, r.Seconds, "⏱️", r.CapturedAt));
        }
        return results;
    }

    // Returns the capture time of whichever read the value came from — the
    // 3x column rescan happens a full OCR pass after the first capture.
    private (string Name, int Seconds, DateTime CapturedAt)? ResolveKind(
        List<(int X, int Y, int W, int H, string Text)> lines, AnchorKind kind, int scale,
        System.Drawing.Rectangle? overlayRect, string? previousName, DateTime capturedAt)
    {
        var resolved = kind == AnchorKind.ProcessingCapacity
            ? _parser.ResolveCapacityTimer(lines)
            : _parser.ResolveAnchoredTimer(lines, kind, previousName);

        // The wide region can be downscaled to 1x on big displays (see above),
        // which is where small value text like Deathstill's "46m 28s" stops
        // OCR-ing even though the anchor label (bigger text) still does. When
        // the anchor was seen but no time came out, re-read just that column
        // at 3x and retry. Fallback only, so normal ticks stay fast.
        if (resolved is null && kind != AnchorKind.ProcessingCapacity && scale < 3
            && TextParserService.FindAnchorLine(lines, kind) is { } anchor)
        {
            if (RescanAnchorColumn(lines, anchor, overlayRect) is { } rescan)
            {
                resolved = _parser.ResolveAnchoredTimer(rescan.Lines, kind, previousName);
                capturedAt = rescan.CapturedAt;
            }
        }

        return resolved is { } r ? (r.Name, r.Seconds, capturedAt) : null;
    }

    // Re-captures a narrow column around the anchor at 3x and returns the
    // original lines with everything inside that column replaced by the
    // sharper read (the station-name tab up top is outside the column, so it
    // stays from the first pass). Null if the capture/crop is unusable.
    private (List<(int X, int Y, int W, int H, string Text)> Lines, DateTime CapturedAt)? RescanAnchorColumn(
        List<(int X, int Y, int W, int H, string Text)> lines,
        (int X, int Y, int W, int H, string Text) anchor,
        System.Drawing.Rectangle? overlayRect)
    {
        int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        int left = Math.Max(0, anchor.X - 250);
        int right = Math.Min(screenWidth, anchor.X + 250);
        int top = Math.Max(0, anchor.Y - anchor.H);
        int bottom = Math.Min(screenHeight, anchor.Y + anchor.H + TextParserService.VerticalPad(anchor.H));
        var crop = new ScanRegion(left, top, right - left, bottom - top);
        if (!crop.IsValid) return null;

        using var bitmap = CaptureAndPreprocess(crop, 3, out var capturedAt);
        if (bitmap is null) return null;

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        using var pix = Pix.LoadFromMemory(ms.ToArray());

        var cropLines = ExtractLines(pix, PageSegMode.SparseText, 3, crop.X, crop.Y, overlayRect);
        Console.WriteLine($"[Scanner] second-pass column read: {string.Join(" | ", cropLines.Select(l => $"\"{l.Text}\""))}");

        var cropRect = new System.Drawing.Rectangle(crop.X, crop.Y, crop.Width, crop.Height);
        var merged = lines
            .Where(l => !cropRect.IntersectsWith(new System.Drawing.Rectangle(l.X, l.Y, l.W, l.H)))
            .Concat(cropLines)
            .OrderBy(l => l.Y)
            .ToList();
        return (merged, capturedAt);
    }

    private static bool IsGameForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var title = new System.Text.StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);
        if (title.ToString().Contains(GameWindowTitleFragment, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.ProcessName.Contains(GameProcessNameFragment, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // process exited mid-check, or access denied (e.g. elevated/anti-cheat-protected process)
        }
    }

    private async void ScanTick(object? sender, EventArgs e)
    {
        if (_isTicking || _isAutoDetecting || _engine is null) return;

        if (!IsGameForeground())
        {
            if (_lastGameFocusState) { _lastGameFocusState = false; ScanInfo?.Invoke("Dune: Awakening isn't focused — scanning paused."); }
            return;
        }
        if (!_lastGameFocusState) { _lastGameFocusState = true; ScanInfo?.Invoke("Dune: Awakening focused — scanning resumed."); }

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
                // parsed[0] is the region's own anchor kind when it resolved
                // (ScanRegionOnce orders it first), so LastName stays that
                // kind's station rather than whichever extra panel was seen.
                _regionState[region.Id] = (Misses: 0, EverMatched: true, LastName: parsed[0].Name);
                foreach (var (itemName, remainingSeconds, icon, capturedAt) in parsed)
                    _timerService.AddOrUpdateAnchoredTimer(region.Id, itemName, remainingSeconds, capturedAt, icon);
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

    // capturedAt is the moment the pixels were grabbed — the time any value
    // read from them was actually true (see AddOrUpdateAnchoredTimer).
    private static Bitmap? CaptureAndPreprocess(ScanRegion region, int scale, out DateTime capturedAt)
    {
        capturedAt = DateTime.Now;
        try
        {
            var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            capturedAt = DateTime.Now;
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
