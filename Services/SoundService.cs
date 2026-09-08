using System.IO;
using System.Media;

namespace DuneTimer.Services;

public class SoundService
{
    private SoundPlayer? _player;

    public SoundService()
    {
        var soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alert.wav");
        if (File.Exists(soundPath))
        {
            _player = new SoundPlayer(soundPath);
            _player.Load();
        }
    }

    public void PlayAlert()
    {
        if (_player is not null)
            _player.Play();
        else
            SystemSounds.Exclamation.Play();
    }
}
