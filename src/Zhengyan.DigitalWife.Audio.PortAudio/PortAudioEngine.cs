using Microsoft.Extensions.Logging;
using PortAudioSharp;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

internal sealed class PortAudioEngine : IDisposable
{
    private readonly ILogger _logger;
    private int _referenceCount;
    private bool _disposed;

    public PortAudioEngine(ILogger logger)
    {
        _logger = logger;
    }

    public void Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.Increment(ref _referenceCount) == 1)
        {
            _logger.LogDebug("Initializing PortAudio.");
            PortAudioSharp.PortAudio.LoadNativeLibrary();
            PortAudioSharp.PortAudio.Initialize();
        }
    }

    public void Release()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Decrement(ref _referenceCount) == 0)
        {
            _logger.LogDebug("Terminating PortAudio.");
            PortAudioSharp.PortAudio.Terminate();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        while (Volatile.Read(ref _referenceCount) > 0)
        {
            Release();
        }
    }
}

