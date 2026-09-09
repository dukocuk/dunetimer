using System.IO;
using System.Text.Json;
using DuneTimer.Models;

namespace DuneTimer.Services;

public class RecipeService
{
    private readonly string _recipePath;
    private readonly string _stationsPath;

    public RecipeService()
    {
        _recipePath = Path.Combine(AppContext.BaseDirectory, "recipes.json");
        _stationsPath = Path.Combine(AppContext.BaseDirectory, "stations.json");
    }

    // Known crafting-station/building names for the "QUEUE" anchor's name
    // resolution. Editable without a rebuild — extend as new refinery/building
    // types are encountered in-game (there's no way to verify exact in-game
    // names without seeing them).
    public List<string> LoadStationNames()
    {
        if (!File.Exists(_stationsPath))
            return ["Medium Ore Refinery", "Deathstill"];

        try
        {
            var json = File.ReadAllText(_stationsPath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("stations", out var stations))
                return JsonSerializer.Deserialize<List<string>>(stations.GetRawText()) ?? [];
            return [];
        }
        catch
        {
            return ["Medium Ore Refinery", "Deathstill"];
        }
    }

    public List<RecipeCategory> LoadRecipes()
    {
        if (!File.Exists(_recipePath))
            return [new RecipeCategory("Custom", [new Recipe { Name = "Custom Timer", DurationSeconds = 60, Icon = "⏱️" }])];

        var json = File.ReadAllText(_recipePath);
        var doc = JsonDocument.Parse(json);
        var categories = new List<RecipeCategory>();

        if (doc.RootElement.TryGetProperty("categories", out var cats))
        {
            foreach (var cat in cats.EnumerateObject())
            {
                var recipes = JsonSerializer.Deserialize<List<Recipe>>(cat.Value.GetRawText()) ?? [];
                categories.Add(new RecipeCategory(cat.Name, recipes));
            }
        }

        return categories;
    }
}
