using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PortAudioSharp;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

public sealed class PortAudioMicrophoneSource : IAudioSource, IDisposable
{
    private readonly PortAudioEngine _engine;
    private readonly ILogger<PortAudioMicrophoneSource> _logger;
    private readonly PortAudioRuntimeOptions _runtimeOptions;
    private bool _disposed;

    public PortAudioMicrophoneSource(
        ILogger<PortAudioMicrophoneSource> logger,
        PortAudioRuntimeOptions runtimeOptions)
    {
        _logger = logger;
        _runtimeOptions = runtimeOptions;
        _engine = new PortAudioEngine(logger);
    }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        AudioCaptureOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        _engine.Acquire();

        var format = new AudioFormat(options.SampleRate, options.Channels);
        var channel = Channel.CreateUnbounded<AudioChunk>();
        var waveWriter = !string.IsNullOrWhiteSpace(options.WaveFilePath)
            ? new PortAudioWaveWriter(options.WaveFilePath!, format)
            : null;
        long totalSamples = 0;

        PortAudioSharp.Stream? stream = null;

        try
        {
            var inputDevice = options.DeviceIndex ?? _runtimeOptions.InputDeviceIndex ?? PortAudioSharp.PortAudio.DefaultInputDevice;
            var input = new StreamParameters
            {
                device = inputDevice,
                channelCount = options.Channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = PortAudioSharp.PortAudio.GetDeviceInfo(inputDevice).defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            stream = new PortAudioSharp.Stream(
                input,
                null,
                options.SampleRate,
                options.FramesPerBuffer == 0 ? PortAudioSharp.PortAudio.FramesPerBufferUnspecified : options.FramesPerBuffer,
                StreamFlags.NoFlag,
                (IntPtr inputPtr, IntPtr outputPtr, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, IntPtr userData) =>
                {
                    try
                    {
                        var sampleCount = checked((int)frameCount * options.Channels);
                        var buffer = new float[sampleCount];
                        System.Runtime.InteropServices.Marshal.Copy(inputPtr, buffer, 0, sampleCount);
                        var offset = TimeSpan.FromSeconds(totalSamples / (double)options.SampleRate / options.Channels);
                        totalSamples += sampleCount;
                        var chunk = new AudioChunk(buffer, format, offset);
                        waveWriter?.Write(buffer);
                        channel.Writer.TryWrite(chunk);
                        return cancellationToken.IsCancellationRequested
                            ? StreamCallbackResult.Complete
                            : StreamCallbackResult.Continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Audio capture callback failed.");
                        channel.Writer.TryComplete(ex);
                        return StreamCallbackResult.Abort;
                    }
                },
                null!);

            stream.Start();

            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            DisposeWaveWriter(waveWriter);
            StopAndDisposeStream(stream);
            ReleaseEngine();
        }
    }

    public async Task<AudioData> RecordAsync(TimeSpan duration, AudioCaptureOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        options ??= new AudioCaptureOptions();

        var samples = new List<float>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(duration);

        try
        {
            await foreach (var chunk in CaptureAsync(options, cts.Token))
            {
                samples.AddRange(chunk.Samples.ToArray());
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }

        return new AudioData(samples.ToArray(), new AudioFormat(options.SampleRate, options.Channels));
    }

    public async Task<AudioData> RecordUntilSilenceAsync(VoiceActivityCaptureOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new VoiceActivityCaptureOptions();

        var preRoll = new ConcurrentQueue<float>();
        var captured = new List<float>();
        var sampleRate = options.SampleRate;
        var channels = options.Channels;
        var preRollSamples = (int)(options.PreRoll.TotalSeconds * sampleRate * channels);
        var minSamples = (int)(options.MinDuration.TotalSeconds * sampleRate * channels);
        var maxSamples = (int)(options.MaxDuration.TotalSeconds * sampleRate * channels);
        var silenceSamplesLimit = (int)(options.SilenceTimeout.TotalSeconds * sampleRate * channels);
        var silenceSamples = 0;
        var started = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await foreach (var chunk in CaptureAsync(options, cts.Token))
            {
                var buffer = chunk.Samples.ToArray();
                var rms = CalculateRms(buffer);
                var isSpeech = rms >= options.SilenceThreshold;

                if (!started)
                {
                    foreach (var sample in buffer)
                    {
                        preRoll.Enqueue(sample);
                        while (preRoll.Count > preRollSamples)
                        {
                            preRoll.TryDequeue(out _);
                        }
                    }

                    if (!isSpeech)
                    {
                        continue;
                    }

                    started = true;
                    captured.AddRange(preRoll);
                    _logger.LogDebug("Voice activity detected; started utterance capture.");
                }

                captured.AddRange(buffer);

                if (isSpeech)
                {
                    silenceSamples = 0;
                }
                else
                {
                    silenceSamples += buffer.Length;
                }

                if (captured.Count >= maxSamples || (captured.Count >= minSamples && silenceSamples >= silenceSamplesLimit))
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }

        return new AudioData(captured.ToArray(), new AudioFormat(sampleRate, channels));
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

    private static float CalculateRms(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0f;
        }

        var sum = 0d;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    private void StopAndDisposeStream(PortAudioSharp.Stream? stream)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            stream.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PortAudio stream stop failed during microphone capture cleanup.");
        }

        try
        {
            stream.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PortAudio stream close failed during microphone capture cleanup.");
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PortAudio stream dispose failed during microphone capture cleanup.");
        }
    }

    private void ReleaseEngine()
    {
        try
        {
            _engine.Release();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PortAudio termination failed during microphone capture cleanup.");
        }
    }

    private void DisposeWaveWriter(PortAudioWaveWriter? waveWriter)
    {
        try
        {
            waveWriter?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Wave writer disposal failed during microphone capture cleanup.");
        }
    }
}
