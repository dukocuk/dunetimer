using System.Windows.Input;
using DuneTimer.Helpers;
using DuneTimer.Models;
using DuneTimer.Services;

namespace DuneTimer.ViewModels;

public class ControlPanelViewModel : ViewModelBase
{
    private readonly TimerService _timerService;
    private readonly RecipeService _recipeService;

    private RecipeCategory? _selectedCategory;
    private Recipe? _selectedRecipe;
    private string _quantity = "1";
    private string _customName = "";
    private string _customMinutes = "";
    private string _customSeconds = "";

    public List<RecipeCategory> Categories { get; }

    public RecipeCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                OnPropertyChanged(nameof(Recipes));
        }
    }

    public List<Recipe>? Recipes => SelectedCategory?.Recipes;

    public Recipe? SelectedRecipe
    {
        get => _selectedRecipe;
        set => SetProperty(ref _selectedRecipe, value);
    }

    public string Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }

    public string CustomName
    {
        get => _customName;
        set => SetProperty(ref _customName, value);
    }

    public string CustomMinutes
    {
        get => _customMinutes;
        set => SetProperty(ref _customMinutes, value);
    }

    public string CustomSeconds
    {
        get => _customSeconds;
        set => SetProperty(ref _customSeconds, value);
    }

    public ICommand StartTimerCommand { get; }

    public Action? CloseWindow { get; set; }

    public ControlPanelViewModel(TimerService timerService, RecipeService recipeService)
    {
        _timerService = timerService;
        _recipeService = recipeService;

        Categories = _recipeService.LoadRecipes();
        SelectedCategory = Categories.FirstOrDefault();

        StartTimerCommand = new RelayCommand(_ => StartTimer());
    }

    private void StartTimer()
    {
        // Custom timer takes priority
        if (!string.IsNullOrWhiteSpace(CustomName))
        {
            int.TryParse(CustomMinutes, out int mins);
            int.TryParse(CustomSeconds, out int secs);
            int total = mins * 60 + secs;
            if (total > 0)
            {
                _timerService.AddTimer(CustomName, total);
                CloseWindow?.Invoke();
                return;
            }
        }

        // Preset recipe
        if (SelectedRecipe is not null)
        {
            int.TryParse(Quantity, out int qty);
            qty = Math.Max(1, qty);
            _timerService.AddTimer(
                SelectedRecipe.Name,
                SelectedRecipe.DurationSeconds,
                SelectedRecipe.Icon,
                qty);
            CloseWindow?.Invoke();
        }
    }
}
