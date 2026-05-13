namespace Zhengyan.DigitalWife.Audio;

public interface IAudioSource
{
    IAsyncEnumerable<AudioChunk> CaptureAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default);

    Task<AudioData> RecordAsync(TimeSpan duration, AudioCaptureOptions? options = null, CancellationToken cancellationToken = default);

    Task<AudioData> RecordUntilSilenceAsync(VoiceActivityCaptureOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IAudioPlayer
{
    Task PlayAsync(AudioData audio, CancellationToken cancellationToken = default);

    Task PlayAsync(IAsyncEnumerable<AudioChunk> audioStream, AudioFormat format, CancellationToken cancellationToken = default);

    Task PlayFileAsync(string path, CancellationToken cancellationToken = default);
}

public interface IAudioPlaybackTiming
{
    TimeSpan GetEstimatedOutputLatency(AudioFormat format);
}

