using System.Text.RegularExpressions;
using DuneTimer.Models;

namespace DuneTimer.Services;

public enum AnchorKind { Queue, ExtractionTime }

public partial class TextParserService
{
    private readonly RecipeService _recipeService;
    private List<Recipe> _allRecipes = [];
    private List<string> _stationNames = [];

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

    public TextParserService(RecipeService recipeService)
    {
        _recipeService = recipeService;
        RefreshRecipes();
    }

    public void RefreshRecipes()
    {
        _allRecipes = _recipeService.LoadRecipes()
            .SelectMany(c => c.Recipes)
            .ToList();
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
        // Search the anchor's own line, then the next few lines below it in
        // the same column, for the first valid time reading — label and
        // value aren't reliably on the same OCR line or immediately adjacent
        // one (e.g. "EXTRACTION" / "TIME" / "23m 54s" are three separate lines).
        for (int i = anchorColIndex; i >= 0 && i < column.Count && i < anchorColIndex + 4; i++)
        {
            seconds = TryExtractTime(column[i].Text, out _);
            if (seconds > 0) break;
        }
        if (seconds <= 0) return null;

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

    public List<(string Name, int Seconds, string Icon)> ParseCraftingText(string ocrText)
    {
        var results = new List<(string, int, string)>();
        var lines = ocrText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            
            // Extract time
            int seconds = TryExtractTime(trimmed, out int matchIndex);
            if (seconds <= 0) continue;

            // Try to match a recipe name in this line or the previous line
            var matchedRecipe = FindBestRecipeMatch(trimmed);
            if (matchedRecipe is null && i > 0)
                matchedRecipe = FindBestRecipeMatch(lines[i - 1].Trim());

            if (matchedRecipe is not null)
            {
                results.Add((matchedRecipe.Name, seconds, matchedRecipe.Icon));
            }
            else
            {
                // Fallback: use text before the time as the name
                string nameCandidate = "";
                if (matchIndex > 0)
                {
                    nameCandidate = trimmed[..matchIndex].Trim().TrimEnd('-', '\u2014', ':', '|', ' ');
                }
                
                if (nameCandidate.Length < 3 && i > 0)
                {
                    nameCandidate = lines[i - 1].Trim();
                }

                if (nameCandidate.Length > 2)
                    results.Add((nameCandidate, seconds, "⏱️"));
            }
        }

        return results;
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

    private Recipe? FindBestRecipeMatch(string text)
    {
        var normalized = text.ToLowerInvariant();

        foreach (var recipe in _allRecipes)
        {
            if (normalized.Contains(recipe.Name.ToLowerInvariant()))
                return recipe;
        }

        foreach (var recipe in _allRecipes)
        {
            var words = recipe.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int matchCount = words.Count(w => normalized.Contains(w));
            if (words.Length > 0 && (double)matchCount / words.Length > 0.7)
                return recipe;
        }

        return null;
    }
}
