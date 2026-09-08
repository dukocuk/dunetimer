using System.IO;
using System.Media;

namespace DuneTimer.Services;

public class SoundService
{
    private readonly SettingsService _settingsService;
    private SoundPlayer? _customPlayer;

    public SoundService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSound();
    }

    public static string DefaultSoundPath => Path.Combine(AppContext.BaseDirectory, "Assets", "alert.wav");

    public bool IsMuted => _settingsService.IsMuted;

    public void ToggleMute() => _settingsService.IsMuted = !_settingsService.IsMuted;

    public void PlayAlert()
    {
        if (_settingsService.IsMuted) return;

        if (_customPlayer is not null)
            _customPlayer.Play();
        else
            SystemSounds.Beep.Play();
    }

    // Called after the user picks a different sound (or resets to default)
    // in Settings, so the next alert uses it without restarting the app.
    public void ReloadSound() => LoadSound();

    // Plays the given file immediately, ignoring mute — used by the Settings
    // "Test" button so the user can preview a sound before saving it.
    public void PreviewSound(string path)
    {
        if (!File.Exists(path)) return;
        using var player = new SoundPlayer(path);
        player.Play();
    }

    private void LoadSound()
    {
        var soundPath = _settingsService.AlertSoundPath;
        if (string.IsNullOrEmpty(soundPath))
            soundPath = DefaultSoundPath;

        if (File.Exists(soundPath))
        {
            _customPlayer = new SoundPlayer(soundPath);
            _customPlayer.Load();
        }
        else
        {
            _customPlayer = null;
        }
    }
}
