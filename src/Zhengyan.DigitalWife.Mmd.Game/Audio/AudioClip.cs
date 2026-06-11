using Silk.NET.OpenAL;

namespace Zhengyan.DigitalWife.Mmd.Game.Audio;

public sealed class AudioClip : IDisposable
{
    private readonly AL _al;
    private bool _disposed;

    internal AudioClip(AL al, uint bufferId, string? name, int channels, int sampleRate, TimeSpan duration)
    {
        _al = al;
        BufferId = bufferId;
        Name = name;
        Channels = channels;
        SampleRate = sampleRate;
        Duration = duration;
    }

    internal uint BufferId { get; }

    public string? Name { get; }

    public int Channels { get; }

    public int SampleRate { get; }

    public TimeSpan Duration { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _al.DeleteBuffer(BufferId);
        GC.SuppressFinalize(this);
    }
}

