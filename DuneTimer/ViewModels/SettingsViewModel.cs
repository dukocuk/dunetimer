using System.IO;
using System.Windows.Input;
using DuneTimer.Helpers;
using DuneTimer.Models;
using DuneTimer.Services;

namespace DuneTimer.ViewModels;

public class HotkeyRowViewModel : ViewModelBase
{
    private HotkeyBinding _binding;

    public string Action { get; }
    public string DisplayName { get; }

    public HotkeyBinding Binding
    {
        get => _binding;
        set => SetProperty(ref _binding, value);
    }

    public HotkeyRowViewModel(string action, HotkeyBinding binding)
    {
        Action = action;
        DisplayName = HotkeyActions.DisplayName(action);
        _binding = binding;
    }
}

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly HotkeyService _hotkeyService;
    private readonly SoundService _soundService;

    private string _soundPath;
    private string _statusMessage = "";
    private bool _hasError;

    public List<HotkeyRowViewModel> HotkeyRows { get; }

    public string SoundDisplayName =>
        string.Equals(SoundPath, SoundService.DefaultSoundPath, StringComparison.OrdinalIgnoreCase)
            ? "Default chime"
            : Path.GetFileName(SoundPath);

    public string SoundPath
    {
        get => _soundPath;
        set
        {
            if (SetProperty(ref _soundPath, value))
                OnPropertyChanged(nameof(SoundDisplayName));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public ICommand BrowseCommand { get; }
    public ICommand TestSoundCommand { get; }
    public ICommand ResetSoundCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Action? CloseWindow { get; set; }

    public SettingsViewModel(SettingsService settingsService, HotkeyService hotkeyService, SoundService soundService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _soundService = soundService;

        var current = _settingsService.HotkeyBindings;
        var defaults = HotkeyActions.Defaults();
        HotkeyRows = HotkeyActions.All
            .Select(action =>
            {
                var source = current.TryGetValue(action, out var b) ? b : defaults[action];
                return new HotkeyRowViewModel(action, new HotkeyBinding { Modifiers = source.Modifiers, Key = source.Key });
            })
            .ToList();

        _soundPath = string.IsNullOrEmpty(_settingsService.AlertSoundPath)
            ? SoundService.DefaultSoundPath
            : _settingsService.AlertSoundPath;

        BrowseCommand = new RelayCommand(_ => Browse());
        TestSoundCommand = new RelayCommand(_ => _soundService.PreviewSound(SoundPath));
        ResetSoundCommand = new RelayCommand(_ => SoundPath = SoundService.DefaultSoundPath);
        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => CloseWindow?.Invoke());
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav",
            Title = "Choose alert sound"
        };
        if (dialog.ShowDialog() == true)
            SoundPath = dialog.FileName;
    }

    private void Save()
    {
        // Catch duplicate combos before touching anything live — Windows
        // would just silently fail the second RegisterHotKey call otherwise.
        var duplicates = HotkeyRows
            .GroupBy(r => (r.Binding.Modifiers, r.Binding.Key))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .Select(r => r.DisplayName)
            .ToList();

        if (duplicates.Count > 0)
        {
            HasError = true;
            StatusMessage = $"Duplicate hotkey assigned to: {string.Join(", ", duplicates)}";
            return;
        }

        var bindings = HotkeyRows.ToDictionary(r => r.Action, r => r.Binding);
        var failed = _hotkeyService.Reregister(bindings);
        _settingsService.HotkeyBindings = bindings;

        _settingsService.AlertSoundPath =
            string.Equals(SoundPath, SoundService.DefaultSoundPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : SoundPath;
        _soundService.ReloadSound();

        if (failed.Count > 0)
        {
            HasError = true;
            StatusMessage = $"Could not register (in use by another app): {string.Join(", ", failed.Select(HotkeyActions.DisplayName))}";
            return;
        }

        CloseWindow?.Invoke();
    }
}
