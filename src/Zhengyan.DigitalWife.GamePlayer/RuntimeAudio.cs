using Zhengyan.DigitalWife.Mmd.Game.Audio;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeAudio
{
    private readonly IReadOnlyDictionary<string, AudioSource> _sources;
    private readonly AudioEngine? _engine;

    internal RuntimeAudio(IReadOnlyDictionary<string, AudioSource> sources, AudioEngine? engine = null)
    {
        _sources = sources;
        _engine = engine;
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

    public void PlayOneShot(string nameOrPath, float volumeScale = 1.0f)
    {
        if (_engine is not null
            && _sources.TryGetValue(nameOrPath, out AudioSource? source)
            && source.Clip is AudioClip clip)
        {
            _engine.PlayOneShot(clip, source.Volume * Math.Clamp(volumeScale, 0.0f, 1.0f));
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
