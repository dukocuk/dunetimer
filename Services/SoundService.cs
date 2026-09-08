using System.IO;
using System.Media;

namespace DuneTimer.Services;

public class SoundService
{
    private readonly SettingsService _settingsService;
    private readonly SoundPlayer? _customPlayer;

    public SoundService(SettingsService settingsService)
    {
        _settingsService = settingsService;

        var soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alert.wav");
        if (File.Exists(soundPath))
        {
            _customPlayer = new SoundPlayer(soundPath);
            _customPlayer.Load();
        }
    }

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
}
