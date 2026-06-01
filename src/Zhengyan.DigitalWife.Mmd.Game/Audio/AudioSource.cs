using System.Numerics;
using Silk.NET.OpenAL;

namespace Zhengyan.DigitalWife.Mmd.Game.Audio;

public sealed class AudioSource : IDisposable
{
    private readonly AL _al;
    private bool _disposed;

    internal AudioSource(AL al, uint sourceId, AudioClip? clip = null)
    {
        _al = al;
        SourceId = sourceId;

        Volume = 1.0f;
        Pitch = 1.0f;
        Position = Vector3.Zero;

        if (clip is not null)
        {
            SetClip(clip);
        }
    }

    internal uint SourceId { get; }

    public AudioClip? Clip { get; private set; }

    public float Volume
    {
        get
        {
            _al.GetSourceProperty(SourceId, SourceFloat.Gain, out float value);
            return value;
        }
        set => _al.SetSourceProperty(SourceId, SourceFloat.Gain, Math.Clamp(value, 0.0f, 4.0f));
    }

    public float Pitch
    {
        get
        {
            _al.GetSourceProperty(SourceId, SourceFloat.Pitch, out float value);
            return value;
        }
        set => _al.SetSourceProperty(SourceId, SourceFloat.Pitch, Math.Clamp(value, 0.01f, 4.0f));
    }

    public bool Looping
    {
        get
        {
            _al.GetSourceProperty(SourceId, SourceBoolean.Looping, out bool value);
            return value;
        }
        set => _al.SetSourceProperty(SourceId, SourceBoolean.Looping, value);
    }

    public Vector3 Position
    {
        get
        {
            _al.GetSourceProperty(SourceId, SourceVector3.Position, out Vector3 value);
            return value;
        }
        set => _al.SetSourceProperty(SourceId, SourceVector3.Position, value);
    }

    public SourceState State
    {
        get
        {
            _al.GetSourceProperty(SourceId, GetSourceInteger.SourceState, out int state);
            return (SourceState)state;
        }
    }

    public void SetClip(AudioClip clip)
    {
        Clip = clip;
        _al.SetSourceProperty(SourceId, SourceInteger.Buffer, clip.BufferId);
    }

    public void Play() => _al.SourcePlay(SourceId);

    public void Pause() => _al.SourcePause(SourceId);

    public void Stop() => _al.SourceStop(SourceId);

    public void Rewind() => _al.SourceRewind(SourceId);

    internal int QueuedBufferCount
    {
        get
        {
            _al.GetSourceProperty(SourceId, GetSourceInteger.BuffersQueued, out int value);
            return value;
        }
    }

    internal int ProcessedBufferCount
    {
        get
        {
            _al.GetSourceProperty(SourceId, GetSourceInteger.BuffersProcessed, out int value);
            return value;
        }
    }

    internal void QueueBuffers(uint[] bufferIds)
    {
        if (bufferIds.Length == 0)
        {
            return;
        }

        _al.SourceQueueBuffers(SourceId, bufferIds);
    }

    internal uint[] UnqueueBuffers(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        uint[] bufferIds = new uint[count];
        _al.SourceUnqueueBuffers(SourceId, bufferIds);
        return bufferIds;
    }

    internal void ClearBufferBinding()
    {
        _al.SetSourceProperty(SourceId, SourceInteger.Buffer, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _al.SourceStop(SourceId);
        _al.DeleteSource(SourceId);
        GC.SuppressFinalize(this);
    }
}

