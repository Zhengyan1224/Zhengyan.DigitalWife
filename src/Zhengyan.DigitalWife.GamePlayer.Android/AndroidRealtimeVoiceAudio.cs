using System.Runtime.CompilerServices;
using Android.Media;
using Zhengyan.DigitalWife.Audio;
using AudioFormat = Zhengyan.DigitalWife.Audio.AudioFormat;
using AudioSource = Android.Media.AudioSource;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidRealtimeVoiceMicrophone : IAudioSource, IDisposable
{
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(AudioCaptureOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using CancellationTokenSource capture = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _captureLock.WaitAsync(capture.Token).ConfigureAwait(false);
        try
        {
            if (options.Channels is not (1 or 2)) throw new NotSupportedException("Android microphone supports mono or stereo capture.");
            ChannelIn channels = options.Channels == 1 ? ChannelIn.Mono : ChannelIn.Stereo;
            int minimum = AudioRecord.GetMinBufferSize(options.SampleRate, channels, Encoding.Pcm16bit);
            if (minimum <= 0) throw new InvalidOperationException($"Android microphone cannot capture at {options.SampleRate} Hz.");
            int sampleCount = checked((int)Math.Clamp(options.FramesPerBuffer == 0 ? 512u : options.FramesPerBuffer, 64u, 8192u) * options.Channels);
            using AudioRecord record = new(AudioSource.Mic, options.SampleRate, channels, Encoding.Pcm16bit, Math.Max(minimum * 2, sampleCount * 2));
            if (record.State != State.Initialized) throw new InvalidOperationException("Android microphone could not be initialized; check recording permission.");
            using CancellationTokenRegistration stop = capture.Token.Register(() => { try { record.Stop(); } catch { } });
            short[] buffer = new short[sampleCount];
            long totalSamples = 0;
            try
            {
                record.StartRecording();
                while (true)
                {
                    capture.Token.ThrowIfCancellationRequested();
                    int count = await Task.Run(() => record.Read(buffer, 0, buffer.Length), CancellationToken.None).ConfigureAwait(false);
                    capture.Token.ThrowIfCancellationRequested();
                    if (count < 0) throw new InvalidOperationException($"Android microphone read failed: {count}.");
                    if (count == 0) continue;
                    float[] samples = new float[count];
                    for (int index = 0; index < count; index++) samples[index] = buffer[index] / 32768f;
                    TimeSpan offset = TimeSpan.FromSeconds((double)totalSamples / options.SampleRate / options.Channels);
                    totalSamples += count;
                    yield return new(samples, new(options.SampleRate, options.Channels), offset);
                }
            }
            finally
            {
                try { record.Stop(); } catch { }
                record.Release();
            }
        }
        finally { _captureLock.Release(); }
    }

    public Task<AudioData> RecordAsync(TimeSpan duration, AudioCaptureOptions? options = null, CancellationToken cancellationToken = default)
        => AndroidVoiceCapture.RecordAsync(this, duration, options ?? new(), cancellationToken);
    public Task<AudioData> RecordUntilSilenceAsync(VoiceActivityCaptureOptions? options = null, CancellationToken cancellationToken = default)
        => AndroidVoiceCapture.RecordUntilSilenceAsync(this, options ?? new(), cancellationToken);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
    }
}

internal sealed class AndroidRealtimeVoiceSpeaker : IAudioPlayer, IDisposable
{
    private readonly SemaphoreSlim _playbackLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public Task PlayAsync(AudioData audio, CancellationToken cancellationToken = default)
        => PlayAsync(audio.ToChunks(cancellationToken: cancellationToken), audio.Format, cancellationToken);

    public async Task PlayAsync(IAsyncEnumerable<AudioChunk> audioStream, AudioFormat format, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using CancellationTokenSource playback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _playbackLock.WaitAsync(playback.Token).ConfigureAwait(false);
        try
        {
            if (format.Channels is not (1 or 2)) throw new NotSupportedException("Android speaker supports mono or stereo playback.");
            ChannelOut channels = format.Channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
            int minimum = AudioTrack.GetMinBufferSize(format.SampleRate, channels, Encoding.Pcm16bit);
            if (minimum <= 0) throw new InvalidOperationException($"Android speaker cannot play at {format.SampleRate} Hz.");
            using AudioAttributes attributes = new AudioAttributes.Builder()!.SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Speech)!.Build()!;
            using global::Android.Media.AudioFormat nativeFormat = new global::Android.Media.AudioFormat.Builder()!
                .SetSampleRate(format.SampleRate)!.SetEncoding(Encoding.Pcm16bit)!.SetChannelMask(channels)!.Build()!;
            int bufferBytes = Math.Max(minimum * 2, 4096);
            using AudioTrack track = new(attributes, nativeFormat, bufferBytes, AudioTrackMode.Stream, AudioManager.AudioSessionIdGenerate);
            if (track.State != AudioTrackState.Initialized) throw new InvalidOperationException("Android speaker could not be initialized.");
            using CancellationTokenRegistration stop = playback.Token.Register(() => { try { track.Pause(); track.Flush(); } catch { } });
            long frames = 0;
            try
            {
                track.Play();
                await foreach (AudioChunk chunk in audioStream.WithCancellation(playback.Token).ConfigureAwait(false))
                {
                    if (chunk.Format.SampleRate != format.SampleRate || chunk.Format.Channels != format.Channels)
                        throw new InvalidDataException("Realtime audio changed format during playback.");
                    short[] samples = new short[chunk.Samples.Length];
                    for (int index = 0; index < samples.Length; index++) samples[index] = (short)Math.Round(Math.Clamp(chunk.Samples.Span[index], -1f, 1f) * short.MaxValue);
                    int offset = 0;
                    while (offset < samples.Length)
                    {
                        playback.Token.ThrowIfCancellationRequested();
                        int written = await Task.Run(() => track.Write(samples, offset, samples.Length - offset, WriteMode.Blocking), CancellationToken.None).ConfigureAwait(false);
                        playback.Token.ThrowIfCancellationRequested();
                        if (written <= 0) throw new InvalidOperationException($"Android speaker write failed: {written}.");
                        offset += written;
                    }
                    frames += samples.Length / format.Channels;
                }
                // Older Android devices wait for a full initial buffer. Pad a
                // short utterance with silence so playback can start there too.
                int bufferFrames = bufferBytes / sizeof(short) / format.Channels;
                if (frames > 0 && frames < bufferFrames)
                {
                    short[] silence = new short[(bufferFrames - (int)frames) * format.Channels];
                    int offset = 0;
                    while (offset < silence.Length)
                    {
                        playback.Token.ThrowIfCancellationRequested();
                        int written = await Task.Run(() => track.Write(silence, offset, silence.Length - offset, WriteMode.Blocking), CancellationToken.None).ConfigureAwait(false);
                        if (written <= 0) throw new InvalidOperationException($"Android speaker padding failed: {written}.");
                        offset += written;
                    }
                }
                // AudioTrack.Write only queues audio. Completion waits until the
                // device has played every frame, as the desktop player does.
                uint previous = 0;
                long wraps = 0;
                while (true)
                {
                    playback.Token.ThrowIfCancellationRequested();
                    uint head = unchecked((uint)track.PlaybackHeadPosition);
                    if (head < previous) wraps += 1L << 32;
                    previous = head;
                    if (wraps + head >= frames) break;
                    await Task.Delay(10, playback.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                try { track.Pause(); track.Flush(); track.Stop(); } catch { }
                track.Release();
            }
        }
        finally { _playbackLock.Release(); }
    }

    public async Task PlayFileAsync(string path, CancellationToken cancellationToken = default)
        => await PlayAsync(await WaveFile.ReadAsync(path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
    }
}
