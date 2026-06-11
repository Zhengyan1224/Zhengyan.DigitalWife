using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PortAudioSharp;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

public sealed class PortAudioSpeakerPlayer : IAudioPlayer, IAudioPlaybackTiming, IDisposable
{
    private readonly PortAudioEngine _engine;
    private readonly ILogger<PortAudioSpeakerPlayer> _logger;
    private readonly PortAudioRuntimeOptions _runtimeOptions;
    private bool _disposed;

    public PortAudioSpeakerPlayer(
        ILogger<PortAudioSpeakerPlayer> logger,
        PortAudioRuntimeOptions runtimeOptions)
    {
        _logger = logger;
        _runtimeOptions = runtimeOptions;
        _engine = new PortAudioEngine(logger);
    }

    public Task PlayAsync(AudioData audio, CancellationToken cancellationToken = default)
        => PlayAsync(audio.ToChunks(cancellationToken: cancellationToken), audio.Format, cancellationToken);

    public async Task PlayAsync(IAsyncEnumerable<AudioChunk> audioStream, AudioFormat format, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audioStream);
        ArgumentNullException.ThrowIfNull(format);

        await PlayStreamingAsync(audioStream, format, cancellationToken);
    }

    public async Task PlayFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var audio = await WaveFile.ReadAsync(path, cancellationToken);
        await PlayAsync(audio, cancellationToken);
    }

    public TimeSpan GetEstimatedOutputLatency(AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        try
        {
            int device = _runtimeOptions.OutputDeviceIndex ?? PortAudioSharp.PortAudio.DefaultOutputDevice;
            double seconds = PortAudioSharp.PortAudio.GetDeviceInfo(device).defaultLowOutputLatency;
            return TimeSpan.FromSeconds(Math.Max(0.0, seconds));
        }
        catch
        {
            return TimeSpan.FromMilliseconds(80);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.Dispose();
    }

    private async Task PlayStreamingAsync(IAsyncEnumerable<AudioChunk> audioStream, AudioFormat format, CancellationToken cancellationToken)
    {
        _engine.Acquire();
        PortAudioSharp.Stream? stream = null;
        var queue = new ConcurrentQueue<float>();
        var producerCompleted = false;
        Exception? producerError = null;

        try
        {
            var producer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var chunk in audioStream.WithCancellation(cancellationToken))
                    {
                        foreach (var sample in chunk.Samples.Span)
                        {
                            queue.Enqueue(sample);
                        }
                    }
                }
                catch (Exception ex)
                {
                    producerError = ex;
                }
                finally
                {
                    producerCompleted = true;
                }
            }, cancellationToken);

            var device = _runtimeOptions.OutputDeviceIndex ?? PortAudioSharp.PortAudio.DefaultOutputDevice;
            var output = new StreamParameters
            {
                device = device,
                channelCount = format.Channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = PortAudioSharp.PortAudio.GetDeviceInfo(device).defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            stream = new PortAudioSharp.Stream(
                null,
                output,
                format.SampleRate,
                PortAudioSharp.PortAudio.FramesPerBufferUnspecified,
                StreamFlags.NoFlag,
                (IntPtr inputPtr, IntPtr outputPtr, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, IntPtr userData) =>
                {
                    var sampleCount = checked((int)frameCount * format.Channels);
                    var buffer = new float[sampleCount];

                    var copied = 0;
                    while (copied < sampleCount && queue.TryDequeue(out var sample))
                    {
                        buffer[copied++] = sample;
                    }

                    System.Runtime.InteropServices.Marshal.Copy(buffer, 0, outputPtr, sampleCount);

                    if (producerCompleted && queue.IsEmpty)
                    {
                        return StreamCallbackResult.Complete;
                    }

                    return StreamCallbackResult.Continue;
                },
                null!);

            stream.Start();
            while ((!producerCompleted || stream.IsActive || !queue.IsEmpty) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(20, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested && stream.IsActive)
            {
                stream.Abort();
            }

            await producer;

            if (producerError is not null)
            {
                throw producerError;
            }
        }
        finally
        {
            stream?.Stop();
            stream?.Close();
            stream?.Dispose();
            _engine.Release();
        }
    }
}

