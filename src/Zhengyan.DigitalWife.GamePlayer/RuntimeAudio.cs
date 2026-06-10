using Zhengyan.DigitalWife.Mmd.Game.Audio;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeAudio
{
    private readonly IReadOnlyDictionary<string, AudioSource> _sources;

    internal RuntimeAudio(IReadOnlyDictionary<string, AudioSource> sources)
    {
        _sources = sources;
    }

    public void Play(string nameOrPath)
    {
        if (_sources.TryGetValue(nameOrPath, out AudioSource? source))
        {
            source.Play();
        }
    }

    public void Pause(string nameOrPath)
    {
        if (_sources.TryGetValue(nameOrPath, out AudioSource? source))
        {
            source.Pause();
        }
    }

    public void Stop(string nameOrPath)
    {
        if (_sources.TryGetValue(nameOrPath, out AudioSource? source))
        {
            source.Stop();
        }
    }

    public void SetVolume(string nameOrPath, float volume)
    {
        if (_sources.TryGetValue(nameOrPath, out AudioSource? source))
        {
            source.Volume = volume;
        }
    }

    public void SetLoop(string nameOrPath, bool loop)
    {
        if (_sources.TryGetValue(nameOrPath, out AudioSource? source))
        {
            source.Looping = loop;
        }
    }

    public bool GetLoop(string nameOrPath)
    {
        return _sources.TryGetValue(nameOrPath, out AudioSource? source) && source.Looping;
    }
}
