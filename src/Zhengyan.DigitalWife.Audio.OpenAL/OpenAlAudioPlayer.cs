using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenAL;

namespace Zhengyan.DigitalWife.Audio.OpenAL;

public sealed class OpenAlAudioPlayer : IAudioPlayer, IAudioPlaybackTiming, IDisposable
{
    private readonly ILogger<OpenAlAudioPlayer> _logger;
    private readonly OpenAlRuntimeOptions _options;
    private readonly AudioContext _context;
    private readonly AL _al;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public OpenAlAudioPlayer(
        ILogger<OpenAlAudioPlayer> logger,
        OpenAlRuntimeOptions options)
    {
        _logger = logger;
        _options = options ?? new OpenAlRuntimeOptions();
        _context = new AudioContext();
        _al = AL.GetApi();

        lock (_sync)
        {
            _context.MakeCurrent();
        }

        _logger.LogInformation("OpenAL playback backend initialized.");
    }

    public TimeSpan GetEstimatedOutputLatency(AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return TimeSpan.FromMilliseconds(Math.Max(1, _options.EstimatedOutputLatencyMilliseconds));
    }

    public async Task PlayAsync(AudioData audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ThrowIfDisposed();

        if (audio.Samples.Length == 0)
        {
            return;
        }

        using CancellationTokenSource linkedCts = CreateLinkedTokenSource(cancellationToken);
        PlaybackHandle handle = WithContext(() => CreateStaticHandle(audio));

        try
        {
            await WaitForCompletionAsync(handle.SourceId, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryStopSource(handle.SourceId);
            throw;
        }
        finally
        {
            TryDisposeHandle(handle);
        }
    }

    public async Task PlayAsync(
        IAsyncEnumerable<AudioChunk> audioStream,
        AudioFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        ArgumentNullException.ThrowIfNull(format);
        ThrowIfDisposed();

        using CancellationTokenSource linkedCts = CreateLinkedTokenSource(cancellationToken);
        ConcurrentQueue<byte[]> pendingBuffers = new();

        Task producer = Task.Run(async () =>
        {
            await foreach (AudioChunk chunk in audioStream.WithCancellation(linkedCts.Token).ConfigureAwait(false))
            {
                if (chunk.Samples.Length == 0)
                {
                    continue;
                }

                pendingBuffers.Enqueue(ConvertToPcm16(chunk.Samples.Span));
            }
        }, linkedCts.Token);

        PlaybackHandle handle = WithContext(CreateStreamingHandle);

        try
        {
            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                if (producer.IsFaulted)
                {
                    await producer.ConfigureAwait(false);
                }

                StreamingSnapshot snapshot = WithContext(() => ProcessStreamingCycle(handle, pendingBuffers, format));
                if (producer.IsCompleted && pendingBuffers.IsEmpty && snapshot.QueuedBufferCount == 0 && snapshot.State != SourceState.Playing)
                {
                    break;
                }

                await Task.Delay(Math.Max(1, _options.PollIntervalMilliseconds), linkedCts.Token).ConfigureAwait(false);
            }

            await producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            linkedCts.Cancel();
            TryStopSource(handle.SourceId);
            throw;
        }
        finally
        {
            await ObserveProducerAsync(producer).ConfigureAwait(false);
            TryDisposeHandle(handle);
        }
    }

    public async Task PlayFileAsync(string path, CancellationToken cancellationToken = default)
    {
        AudioData audio = await WaveFile.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        await PlayAsync(audio, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();

        lock (_sync)
        {
            try
            {
                _context.MakeCurrent();
            }
            catch
            {
            }

            _al.Dispose();
            _context.Dispose();
        }
    }

    private PlaybackHandle CreateStaticHandle(AudioData audio)
    {
        byte[] pcm16 = ConvertToPcm16(audio.Samples.AsSpan());
        uint bufferId = CreateBuffer(pcm16, audio.Format.SampleRate, audio.Format.Channels);
        uint sourceId = _al.GenSource();
        _al.SetSourceProperty(sourceId, SourceInteger.Buffer, bufferId);
        _al.SourcePlay(sourceId);
        return new PlaybackHandle(sourceId, [bufferId], isStreaming: false);
    }

    private PlaybackHandle CreateStreamingHandle()
    {
        return new PlaybackHandle(_al.GenSource(), [], isStreaming: true);
    }

    private StreamingSnapshot ProcessStreamingCycle(
        PlaybackHandle handle,
        ConcurrentQueue<byte[]> pendingBuffers,
        AudioFormat format)
    {
        CleanupProcessedBuffers(handle);

        int queuedBufferCount = GetSourceIntValue(handle.SourceId, GetSourceInteger.BuffersQueued);
        while (queuedBufferCount < Math.Max(1, _options.MaxQueuedStreamingBuffers) && pendingBuffers.TryDequeue(out byte[]? pcm16))
        {
            uint bufferId = CreateBuffer(pcm16, format.SampleRate, format.Channels);
            _al.SourceQueueBuffers(handle.SourceId, [bufferId]);
            handle.BufferIds.Add(bufferId);
            queuedBufferCount++;
        }

        SourceState state = GetSourceState(handle.SourceId);
        if (queuedBufferCount > 0 && state != SourceState.Playing)
        {
            _al.SourcePlay(handle.SourceId);
            state = GetSourceState(handle.SourceId);
        }

        return new StreamingSnapshot(queuedBufferCount, state);
    }

    private void CleanupProcessedBuffers(PlaybackHandle handle)
    {
        int processed = GetSourceIntValue(handle.SourceId, GetSourceInteger.BuffersProcessed);
        if (processed <= 0)
        {
            return;
        }

        uint[] released = new uint[processed];
        _al.SourceUnqueueBuffers(handle.SourceId, released);
        foreach (uint bufferId in released)
        {
            handle.BufferIds.Remove(bufferId);
            _al.DeleteBuffer(bufferId);
        }
    }

    private void TryStopSource(uint sourceId)
    {
        try
        {
            WithContext(() => _al.SourceStop(sourceId));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WaitForCompletionAsync(uint sourceId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceState state = WithContext(() => GetSourceState(sourceId));
            if (state != SourceState.Playing && state != SourceState.Initial)
            {
                return;
            }

            await Task.Delay(Math.Max(1, _options.PollIntervalMilliseconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private void TryDisposeHandle(PlaybackHandle handle)
    {
        try
        {
            WithContext(() => DisposeHandle(handle));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeHandle(PlaybackHandle handle)
    {
        try
        {
            _al.SourceStop(handle.SourceId);
        }
        catch
        {
        }

        if (handle.IsStreaming)
        {
            try
            {
                int queued = GetSourceIntValue(handle.SourceId, GetSourceInteger.BuffersQueued);
                if (queued > 0)
                {
                    uint[] released = new uint[queued];
                    _al.SourceUnqueueBuffers(handle.SourceId, released);
                    foreach (uint bufferId in released)
                    {
                        handle.BufferIds.Remove(bufferId);
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            _al.SetSourceProperty(handle.SourceId, SourceInteger.Buffer, 0);
        }
        catch
        {
        }

        foreach (uint bufferId in handle.BufferIds)
        {
            try
            {
                _al.DeleteBuffer(bufferId);
            }
            catch
            {
            }
        }

        handle.BufferIds.Clear();

        try
        {
            _al.DeleteSource(handle.SourceId);
        }
        catch
        {
        }
    }

    private async Task ObserveProducerAsync(Task producer)
    {
        try
        {
            await producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private uint CreateBuffer(byte[] pcm16, int sampleRate, int channels)
    {
        uint bufferId = _al.GenBuffer();
        _al.BufferData(bufferId, GetBufferFormat(channels), pcm16, sampleRate);
        return bufferId;
    }

    private SourceState GetSourceState(uint sourceId)
    {
        return (SourceState)GetSourceIntValue(sourceId, GetSourceInteger.SourceState);
    }

    private int GetSourceIntValue(uint sourceId, GetSourceInteger parameter)
    {
        _al.GetSourceProperty(sourceId, parameter, out int value);
        return value;
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private TResult WithContext<TResult>(Func<TResult> action)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _context.MakeCurrent();
            return action();
        }
    }

    private void WithContext(Action action)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _context.MakeCurrent();
            action();
        }
    }

    private static byte[] ConvertToPcm16(ReadOnlySpan<float> samples)
    {
        short[] pcm16Samples = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            pcm16Samples[i] = (short)MathF.Round(Math.Clamp(samples[i], -1.0f, 1.0f) * short.MaxValue);
        }

        byte[] pcm16 = new byte[pcm16Samples.Length * sizeof(short)];
        Buffer.BlockCopy(pcm16Samples, 0, pcm16, 0, pcm16.Length);
        return pcm16;
    }

    private static BufferFormat GetBufferFormat(int channels)
    {
        return channels switch
        {
            1 => BufferFormat.Mono16,
            2 => BufferFormat.Stereo16,
            _ => throw new NotSupportedException($"Unsupported channel count: {channels}.")
        };
    }

    private sealed class PlaybackHandle(uint sourceId, List<uint> bufferIds, bool isStreaming)
    {
        public uint SourceId { get; } = sourceId;

        public List<uint> BufferIds { get; } = bufferIds;

        public bool IsStreaming { get; } = isStreaming;
    }

    private readonly record struct StreamingSnapshot(int QueuedBufferCount, SourceState State);
}
