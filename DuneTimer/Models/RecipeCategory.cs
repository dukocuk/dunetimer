namespace DuneTimer.Models;

public class RecipeCategory
{
    public string Name { get; set; } = "";
    public List<Recipe> Recipes { get; set; } = [];

    public RecipeCategory() { }
    public RecipeCategory(string name, List<Recipe> recipes)
    {
        Name = name;
        Recipes = recipes;
    }
}
