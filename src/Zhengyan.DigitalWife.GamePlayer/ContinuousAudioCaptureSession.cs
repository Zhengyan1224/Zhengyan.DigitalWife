using System.Threading.Channels;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class ContinuousAudioCaptureSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Channel<float[]> _chunks;
    private readonly AudioFormat _format;
    private readonly Task _pumpTask;
    private float[] _pendingSamples = [];
    private int _pendingOffset;
    private bool _disposed;

    public ContinuousAudioCaptureSession(
        IAudioSource audioSource,
        AudioCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audioSource);
        ArgumentNullException.ThrowIfNull(options);

        _format = new AudioFormat(Math.Max(1, options.SampleRate), Math.Max(1, options.Channels));
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _chunks = Channel.CreateBounded<float[]>(new BoundedChannelOptions(ResolveChunkCapacity(options))
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _pumpTask = Task.Run(() => PumpAsync(audioSource, options), CancellationToken.None);
    }

    public async Task<AudioData> ReadAsync(
        TimeSpan duration,
        CancellationToken cancellationToken,
        bool discardBufferedAudio = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (duration <= TimeSpan.Zero)
        {
            return new AudioData([], _format);
        }

        cancellationToken.ThrowIfCancellationRequested();

        long targetSamples = Math.Max(
            1,
            (long)Math.Round(duration.TotalSeconds * _format.SampleRate * _format.Channels));
        if (targetSamples > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Audio capture segment is too large.");
        }

        if (discardBufferedAudio)
        {
            ClearBufferedAudio();
        }

        var samples = new List<float>((int)targetSamples);
        ConsumePendingSamples(samples, (int)targetSamples);
        while (samples.Count < targetSamples)
        {
            if (_chunks.Reader.TryRead(out float[]? chunkSamples))
            {
                AppendChunkSamples(chunkSamples, samples, (int)targetSamples);
                ConsumePendingSamples(samples, (int)targetSamples);
                continue;
            }

            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }

        return new AudioData(samples.ToArray(), _format);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task PumpAsync(IAudioSource audioSource, AudioCaptureOptions options)
    {
        Exception? failure = null;
        try
        {
            await foreach (AudioChunk chunk in audioSource.CaptureAsync(options, _cts.Token).ConfigureAwait(false))
            {
                _chunks.Writer.TryWrite(chunk.Samples.ToArray());
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _chunks.Writer.TryComplete(failure);
        }
    }

    private void ClearBufferedAudio()
    {
        _pendingSamples = [];
        _pendingOffset = 0;
        while (_chunks.Reader.TryRead(out _))
        {
        }
    }

    private void ConsumePendingSamples(List<float> samples, int targetSamples)
    {
        while (_pendingOffset < _pendingSamples.Length && samples.Count < targetSamples)
        {
            samples.Add(_pendingSamples[_pendingOffset]);
            _pendingOffset++;
        }

        if (_pendingOffset >= _pendingSamples.Length)
        {
            _pendingSamples = [];
            _pendingOffset = 0;
        }
    }

    private void AppendChunkSamples(float[] chunkSamples, List<float> samples, int targetSamples)
    {
        int remaining = targetSamples - samples.Count;
        int take = Math.Min(chunkSamples.Length, remaining);
        for (var index = 0; index < take; index++)
        {
            samples.Add(chunkSamples[index]);
        }

        if (take < chunkSamples.Length)
        {
            _pendingSamples = chunkSamples;
            _pendingOffset = take;
        }
    }

    private static int ResolveChunkCapacity(AudioCaptureOptions options)
    {
        int sampleRate = Math.Max(1, options.SampleRate);
        int channels = Math.Max(1, options.Channels);
        int framesPerBuffer = options.FramesPerBuffer == 0
            ? 512
            : (int)Math.Clamp(options.FramesPerBuffer, 64u, 8192u);
        long chunkSamples = Math.Max(1L, (long)framesPerBuffer * channels);
        long targetSamples = (long)Math.Ceiling(sampleRate * channels * 4.0);
        long capacity = (targetSamples / chunkSamples) + 4;
        return (int)Math.Clamp(capacity, 8, 256);
    }
}
