using System.Collections.Concurrent;
using Zhengyan.DigitalWife.Audio;
using Silk.NET.OpenAL;

namespace Zhengyan.DigitalWife.Mmd.Game.Audio;

public sealed class GameAudioPlayer : IAudioPlayer, IAudioPlaybackTiming, IDisposable
{
    private readonly Func<AudioEngine?> _audioAccessor;
    private readonly Func<Action, Task> _runOnAudioThreadAsync;
    private readonly Func<string?>? _audioUnavailableMessageAccessor;
    private readonly int _estimatedOutputLatencyMilliseconds;
    private readonly int _maxQueuedStreamingBuffers;
    private readonly int _pollIntervalMilliseconds;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public GameAudioPlayer(
        Func<AudioEngine?> audioAccessor,
        Func<Action, Task> runOnAudioThreadAsync,
        Func<string?>? audioUnavailableMessageAccessor = null,
        int estimatedOutputLatencyMilliseconds = 60,
        int maxQueuedStreamingBuffers = 8,
        int pollIntervalMilliseconds = 10)
    {
        _audioAccessor = audioAccessor ?? throw new ArgumentNullException(nameof(audioAccessor));
        _runOnAudioThreadAsync = runOnAudioThreadAsync ?? throw new ArgumentNullException(nameof(runOnAudioThreadAsync));
        _audioUnavailableMessageAccessor = audioUnavailableMessageAccessor;
        _estimatedOutputLatencyMilliseconds = Math.Max(1, estimatedOutputLatencyMilliseconds);
        _maxQueuedStreamingBuffers = Math.Max(1, maxQueuedStreamingBuffers);
        _pollIntervalMilliseconds = Math.Max(1, pollIntervalMilliseconds);
    }

    public TimeSpan GetEstimatedOutputLatency(AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return TimeSpan.FromMilliseconds(_estimatedOutputLatencyMilliseconds);
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
        PlaybackHandle handle = await InvokeOnAudioThreadAsync(() =>
        {
            AudioEngine engine = RequireAudio();
            AudioClip clip = engine.CreateClip(
                $"audio:{Guid.NewGuid():N}",
                audio.Samples.AsSpan(),
                audio.Format.SampleRate,
                audio.Format.Channels);
            AudioSource source = engine.CreateSource(clip);
            source.Play();
            return new PlaybackHandle(engine, source, clip, [], isStreaming: false);
        }).ConfigureAwait(false);

        try
        {
            await WaitForCompletionAsync(handle.Source, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryStopSourceAsync(handle.Source).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await TryDisposeHandleAsync(handle).ConfigureAwait(false);
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

        PlaybackHandle handle = await InvokeOnAudioThreadAsync(() =>
        {
            AudioEngine engine = RequireAudio();
            return new PlaybackHandle(engine, engine.CreateSource(), null, [], isStreaming: true);
        }).ConfigureAwait(false);

        try
        {
            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                if (producer.IsFaulted)
                {
                    await producer.ConfigureAwait(false);
                }

                StreamingSnapshot snapshot = await InvokeOnAudioThreadAsync(() =>
                    ProcessStreamingCycle(handle, pendingBuffers, format)).ConfigureAwait(false);

                if (producer.IsCompleted && pendingBuffers.IsEmpty && snapshot.QueuedBufferCount == 0 && snapshot.State != SourceState.Playing)
                {
                    break;
                }

                await Task.Delay(_pollIntervalMilliseconds, linkedCts.Token).ConfigureAwait(false);
            }

            await producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            linkedCts.Cancel();
            await TryStopSourceAsync(handle.Source).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await ObserveProducerAsync(producer).ConfigureAwait(false);
            await TryDisposeHandleAsync(handle).ConfigureAwait(false);
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
    }

    private StreamingSnapshot ProcessStreamingCycle(
        PlaybackHandle handle,
        ConcurrentQueue<byte[]> pendingBuffers,
        AudioFormat format)
    {
        CleanupProcessedBuffers(handle);

        int queuedBufferCount = handle.Source.QueuedBufferCount;
        while (queuedBufferCount < _maxQueuedStreamingBuffers && pendingBuffers.TryDequeue(out byte[]? pcm16))
        {
            uint bufferId = handle.Engine.CreateBuffer(pcm16, format.SampleRate, format.Channels);
            handle.Source.QueueBuffers([bufferId]);
            handle.BufferIds.Add(bufferId);
            queuedBufferCount++;
        }

        SourceState state = handle.Source.State;
        if (queuedBufferCount > 0 && state != SourceState.Playing)
        {
            handle.Source.Play();
            state = handle.Source.State;
        }

        return new StreamingSnapshot(queuedBufferCount, state);
    }

    private void CleanupProcessedBuffers(PlaybackHandle handle)
    {
        int processed = handle.Source.ProcessedBufferCount;
        if (processed <= 0)
        {
            return;
        }

        foreach (uint bufferId in handle.Source.UnqueueBuffers(processed))
        {
            handle.BufferIds.Remove(bufferId);
            handle.Engine.DeleteBuffer(bufferId);
        }
    }

    private async Task WaitForCompletionAsync(AudioSource source, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceState state = await InvokeOnAudioThreadAsync(() => source.State).ConfigureAwait(false);
            if (state != SourceState.Playing && state != SourceState.Initial)
            {
                return;
            }

            await Task.Delay(_pollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryStopSourceAsync(AudioSource source)
    {
        try
        {
            await InvokeOnAudioThreadAsync(source.Stop).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task TryDisposeHandleAsync(PlaybackHandle handle)
    {
        try
        {
            await InvokeOnAudioThreadAsync(() => DisposeHandle(handle)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeHandle(PlaybackHandle handle)
    {
        try
        {
            handle.Source.Stop();
        }
        catch
        {
        }

        if (handle.IsStreaming)
        {
            try
            {
                int queued = handle.Source.QueuedBufferCount;
                if (queued > 0)
                {
                    foreach (uint bufferId in handle.Source.UnqueueBuffers(queued))
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
            handle.Source.ClearBufferBinding();
        }
        catch
        {
        }

        foreach (uint bufferId in handle.BufferIds)
        {
            try
            {
                handle.Engine.DeleteBuffer(bufferId);
            }
            catch
            {
            }
        }

        handle.BufferIds.Clear();

        try
        {
            handle.Source.Dispose();
        }
        catch
        {
        }

        try
        {
            handle.Clip?.Dispose();
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

    private AudioEngine RequireAudio()
    {
        AudioEngine? audio = _audioAccessor();
        if (audio is not null)
        {
            return audio;
        }

        throw new InvalidOperationException(_audioUnavailableMessageAccessor?.Invoke() ?? "Audio is unavailable.");
    }

    private async Task<T> InvokeOnAudioThreadAsync<T>(Func<T> action)
    {
        ThrowIfDisposed();

        T? result = default;
        await _runOnAudioThreadAsync(() => result = action()).ConfigureAwait(false);
        return result!;
    }

    private Task InvokeOnAudioThreadAsync(Action action)
    {
        ThrowIfDisposed();
        return _runOnAudioThreadAsync(action);
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

    private sealed class PlaybackHandle(
        AudioEngine engine,
        AudioSource source,
        AudioClip? clip,
        List<uint> bufferIds,
        bool isStreaming)
    {
        public AudioEngine Engine { get; } = engine;

        public AudioSource Source { get; } = source;

        public AudioClip? Clip { get; } = clip;

        public List<uint> BufferIds { get; } = bufferIds;

        public bool IsStreaming { get; } = isStreaming;
    }

    private readonly record struct StreamingSnapshot(int QueuedBufferCount, SourceState State);
}
