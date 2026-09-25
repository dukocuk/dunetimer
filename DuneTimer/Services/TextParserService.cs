using System.Text.RegularExpressions;
using DuneTimer.Models;

namespace DuneTimer.Services;

public enum AnchorKind { Queue, ExtractionTime, ProcessingCapacity }

public partial class TextParserService
{
    private readonly RecipeService _recipeService;
    private List<string> _stationNames = [];

    // Processing rate is never OCR'd — confirmed by direct testing that the
    // "PROCESSING RATE"/"X ml/s" line fails to OCR at all (not even garbled)
    // across every attempt, unlike the capacity value beside it which reads
    // cleanly every time. It's a fixed stat of the building tier, not
    // something that changes at runtime, so it's hardcoded here instead.
    private static readonly Dictionary<string, double> ProcessingRatesMlPerSec = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blood Purifier"] = 5,
        ["Improved Blood Purifier"] = 8,
    };

    // Also used as a sanity ceiling in ResolveCapacityTimer: OCR occasionally
    // misreads the total line's leading '/' as a stray digit (e.g. "/ 24,000 ml"
    // becomes "724,000 ml"), which then looks like a perfectly valid bare
    // value line. A corrupted total is always at/near this max, while a
    // genuine draining current-value never reaches it, so any candidate >=
    // max gets rejected as a probable corrupted-total read instead of
    // producing a wildly-wrong multi-hour timer.
    private static readonly Dictionary<string, double> ProcessingMaxCapacityMl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blood Purifier"] = 6000,
        ["Improved Blood Purifier"] = 24000,
    };

    // Matches 01:23:45 or 45:30
    [GeneratedRegex(@"\b(?:(\d{1,2}):)?(\d{1,2}):(\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex ColonTimePattern();

    // Matches 1h 30m 45s, 30m 45s, 45s, 1h, 30m
    [GeneratedRegex(@"\b(?:(\d+)\s*[hH])?\s*(?:(\d+)\s*[mM])?\s*(?:(\d+)\s*[sS])?\b", RegexOptions.Compiled)]
    private static partial Regex LetterTimePattern();

    [GeneratedRegex(@"\bQUEUE\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex QueueAnchorPattern();

    // Anchors on "EXTRACTION" alone, not the full "EXTRACTION TIME" phrase —
    // that label wraps across two separate OCR lines ("EXTRACTION" / "TIME")
    // in-game, so a same-line phrase match would never fire.
    [GeneratedRegex(@"\bEXTRACTION\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ExtractionAnchorPattern();

    // "PROCESSING" alone (like "EXTRACTION" above) — never appears in the
    // sibling WATER CAPACITY / CIRCUIT CAPACITY labels on the same panel.
    // Deliberately case-SENSITIVE (no IgnoreCase): confirmed via live testing
    // that Deathstill has its own unrelated "STATUS: Processing" value field,
    // OCR'd in mixed/title case, which false-matched this anchor when it was
    // case-insensitive and caused the resolver to run on the wrong panel. The
    // real "PROCESSING CAPACITY" label always OCRs as literal all-caps, like
    // every other anchor label in this UI (QUEUE, EXTRACTION).
    [GeneratedRegex(@"\bPROCESSING\b", RegexOptions.Compiled)]
    private static partial Regex ProcessingCapacityAnchorPattern();

    // A bare "<digits,with,commas> ml" line — the current capacity value.
    // Deliberately anchored start-to-end so it doesn't match the total line
    // beneath it ("/ 24,000 ml"), which callers primarily exclude by its
    // leading '/' (see ProcessingMaxCapacityMl for the backstop when that
    // slash itself gets OCR'd into a digit).
    [GeneratedRegex(@"^([\d,]+)\s*ml$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CapacityValuePattern();

    public TextParserService(RecipeService recipeService)
    {
        _recipeService = recipeService;
        RefreshRecipes();
    }

    public void RefreshRecipes()
    {
        // Longest-first so a more specific name (e.g. "Medium Ore Refinery")
        // wins over a shorter one that could also match (e.g. "Ore Refinery").
        _stationNames = _recipeService.LoadStationNames()
            .OrderByDescending(n => n.Length)
            .ToList();
    }

    public static AnchorKind? MatchAnchor(string line)
    {
        if (QueueAnchorPattern().IsMatch(line)) return AnchorKind.Queue;
        if (ExtractionAnchorPattern().IsMatch(line)) return AnchorKind.ExtractionTime;
        if (ProcessingCapacityAnchorPattern().IsMatch(line)) return AnchorKind.ProcessingCapacity;
        return null;
    }

    // How far below an anchor line its value can sit. Shared by the region
    // builder (so the value is inside the capture) and the resolver (so it's
    // inside the search) — scaled off the anchor's own text height so it
    // adapts to resolution/UI scale, with a generous floor.
    public static int VerticalPad(int anchorHeight) => Math.Max(anchorHeight * 10, 250);

    public static (int X, int Y, int W, int H, string Text)? FindAnchorLine(
        IReadOnlyList<(int X, int Y, int W, int H, string Text)> lines, AnchorKind kind)
    {
        foreach (var l in lines)
            if (MatchAnchor(l.Text) == kind) return l;
        return null;
    }

    // lines must be pre-sorted top-to-bottom by Y (caller's responsibility —
    // OCR iteration order isn't reliable for this). The region these lines
    // come from spans from the top of the screen (to see the highlighted
    // panel tab, e.g. "Medium Ore Refinery" / "Deathstill") down through the
    // anchor, at full screen width.
    public (string Name, int Seconds)? ResolveAnchoredTimer(
        IReadOnlyList<(int X, int Y, int W, int H, string Text)> lines, AnchorKind kind, string? previousName = null)
    {
        int anchorIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (MatchAnchor(lines[i].Text) == kind)
            {
                anchorIndex = i;
                break;
            }
        }
        if (anchorIndex < 0) return null;
        var anchor = lines[anchorIndex];

        // Restrict the time search to the anchor's own UI column. The region
        // is full screen width (needed to also see the tab name, which can
        // sit anywhere across the top bar), so a pure top-to-bottom scan can
        // land on an unrelated column at a similar height — e.g. Deathstill's
        // "WATER CAPACITY: 108,228 ml" sits at roughly the same Y as
        // "EXTRACTION TIME: 23m 54s" in a parallel panel.
        var column = lines.Where(l => Math.Abs(l.X - anchor.X) < 250).OrderBy(l => l.Y).ToList();
        int anchorColIndex = column.FindIndex(l => MatchAnchor(l.Text) == kind);

        int seconds = 0;
        // Search the anchor's own line, then every line below it in the same
        // column within the anchor's vertical pad (same bound as
        // BuildAnchorRegion), for the first valid time reading — label and
        // value aren't reliably on the same OCR line or immediately adjacent
        // one (e.g. "EXTRACTION" / "TIME" / "23m 54s" are three separate
        // lines, and Deathstill can put other rows between them).
        int maxY = anchor.Y + anchor.H + VerticalPad(anchor.H);
        for (int i = anchorColIndex; i >= 0 && i < column.Count && column[i].Y <= maxY; i++)
        {
            seconds = TryExtractTime(column[i].Text, out _);
            if (seconds > 0) break;
        }
        if (seconds <= 0)
        {
            Console.WriteLine($"[Scanner] {kind} anchor at ({anchor.X},{anchor.Y}) found but no time in its column; lines searched: "
                + string.Join(" | ", column.Where(l => l.Y >= anchor.Y && l.Y <= maxY).Select(l => $"\"{l.Text}\"")));
            return null;
        }

        // Name is the best known station/building-name match found anywhere
        // in the captured lines (the tab sits above the anchor, same list for
        // both anchors — e.g. "Medium Ore Refinery" or "Deathstill").
        foreach (var station in _stationNames)
        {
            if (lines.Any(l => l.Text.Contains(station, StringComparison.OrdinalIgnoreCase)))
                return (station, seconds);
        }

        // A transient single-tick miss on the station name (OCR flake, not a
        // real station change) reuses whichever real name this region last
        // resolved, instead of falling back to the generic literal — that
        // literal would otherwise register as a brand-new distinct timer
        // under the (region, name) identity used by AddOrUpdateAnchoredTimer.
        if (previousName is not null) return (previousName, seconds);

        string fallback = kind == AnchorKind.ExtractionTime ? "Extraction Time" : "Crafting Queue";
        Console.WriteLine($"[AutoDetect] {kind} found but no known station/building name matched — add it to stations.json");
        return (fallback, seconds);
    }

    // Blood Purifier-style panels never display a countdown — only a
    // draining "processing capacity" value and a rate that reliably fails to
    // OCR (see ProcessingRatesMlPerSec). So instead of extracting a time
    // directly like ResolveAnchoredTimer, this reads the capacity value and
    // computes time-remaining from the hardcoded rate for whichever known
    // station name is present. There's no generic-literal fallback like
    // ResolveAnchoredTimer has: without a resolved name there's no way to
    // know which rate applies, so an unresolved name just yields no timer.
    public (string Name, int Seconds)? ResolveCapacityTimer(
        IReadOnlyList<(int X, int Y, int W, int H, string Text)> lines)
    {
        int anchorIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (MatchAnchor(lines[i].Text) == AnchorKind.ProcessingCapacity)
            {
                anchorIndex = i;
                break;
            }
        }
        if (anchorIndex < 0) return null;
        var anchor = lines[anchorIndex];

        // Resolve the station name first (not the value) so the max-capacity
        // sanity check below can use the right ceiling for this station —
        // there's no generic-literal fallback here, since without a name
        // there's no rate/max to compute against.
        string? station = null;
        double rate = 0, maxMl = 0;
        foreach (var candidate in _stationNames)
        {
            if (!ProcessingRatesMlPerSec.TryGetValue(candidate, out rate)) continue;
            if (!lines.Any(l => l.Text.Contains(candidate, StringComparison.OrdinalIgnoreCase))) continue;
            station = candidate;
            maxMl = ProcessingMaxCapacityMl[candidate];
            break;
        }
        if (station is null) return null;

        // Same column-restriction rationale as ResolveAnchoredTimer: this
        // region is full screen width, so an unrestricted scan could land on
        // WATER CAPACITY's value at a similar height instead.
        var column = lines.Where(l => Math.Abs(l.X - anchor.X) < 250).OrderBy(l => l.Y).ToList();
        int anchorColIndex = column.FindIndex(l => MatchAnchor(l.Text) == AnchorKind.ProcessingCapacity);

        double? currentMl = null;
        for (int i = anchorColIndex; i >= 0 && i < column.Count && i < anchorColIndex + 6; i++)
        {
            var text = column[i].Text.Trim();
            if (text.Contains('/')) continue; // the total line ("/ 24,000 ml"), not the current value
            var match = CapacityValuePattern().Match(text);
            if (!match.Success) continue;

            var value = double.Parse(match.Groups[1].Value.Replace(",", ""));
            // A corrupted total line (leading '/' OCR'd into a stray digit,
            // e.g. "724,000 ml") looks identical to a valid bare value line
            // once the slash is gone — reject anything at/above the known
            // max instead of accepting it as a plausible current reading.
            if (value >= maxMl) continue;

            currentMl = value;
            break;
        }
        if (currentMl is not { } ml || ml <= 0) return null;

        int seconds = (int)Math.Round(ml / rate);
        return seconds > 0 ? (station, seconds) : null;
    }

    public static int TryExtractTime(string text, out int matchIndex)
    {
        matchIndex = -1;

        var colonMatch = ColonTimePattern().Match(text);
        if (colonMatch.Success)
        {
            matchIndex = colonMatch.Index;
            int h = colonMatch.Groups[1].Success ? int.Parse(colonMatch.Groups[1].Value) : 0;
            int m = int.Parse(colonMatch.Groups[2].Value);
            int s = int.Parse(colonMatch.Groups[3].Value);
            return h * 3600 + m * 60 + s;
        }

        var letterMatches = LetterTimePattern().Matches(text);
        foreach (Match m in letterMatches)
        {
            if (m.Value.Trim().Length == 0) continue;

            matchIndex = m.Index;
            int h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
            int mins = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            int secs = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;

            int total = h * 3600 + mins * 60 + secs;
            if (total > 0) return total;
        }

        return 0;
    }
}
