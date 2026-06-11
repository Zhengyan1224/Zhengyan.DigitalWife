namespace Zhengyan.DigitalWife.Speech;

using Zhengyan.DigitalWife.Audio;

public class SpeechRecognitionOptions
{
    public string? Language { get; init; }

    public bool EnableTimestamps { get; init; }

    public bool TranslateToEnglish { get; init; }
}

public class StreamingSpeechRecognitionOptions : SpeechRecognitionOptions
{
    public TimeSpan PartialResultInterval { get; init; } = TimeSpan.FromSeconds(1.5);
}

public sealed record SpeechRecognitionSegment(string Text, TimeSpan Start, TimeSpan End);

public sealed class SpeechRecognitionResult
{
    public required string Text { get; init; }

    public string? Language { get; init; }

    public IReadOnlyList<SpeechRecognitionSegment> Segments { get; init; } = [];
}

public sealed class SpeechRecognitionUpdate
{
    public required string Text { get; init; }

    public bool IsFinal { get; init; }

    public TimeSpan Offset { get; init; }

    public IReadOnlyList<SpeechRecognitionSegment> Segments { get; init; } = [];
}

public interface IStreamingSpeechRecognitionSession : IAsyncDisposable
{
    ValueTask WriteAsync(AudioChunk chunk, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<SpeechRecognitionUpdate> GetUpdatesAsync(CancellationToken cancellationToken = default);
}

public interface ISpeechRecognizer
{
    string Name { get; }

    Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio, SpeechRecognitionOptions? options = null, CancellationToken cancellationToken = default);

    Task<SpeechRecognitionResult> RecognizeFileAsync(string path, SpeechRecognitionOptions? options = null, CancellationToken cancellationToken = default);

    IStreamingSpeechRecognitionSession CreateStreamingSession(StreamingSpeechRecognitionOptions? options = null);
}

public sealed class SpeechSynthesisOptions
{
    public string? Voice { get; init; }

    public SpeechSynthesisModelKind? ModelKind { get; init; }

    public float Speed { get; init; } = 1.0f;

    public int SpeakerId { get; init; }

    public int StreamChunkSamples { get; init; } = 4096;
}

public enum SpeechSynthesisModelKind
{
    Vits,
    Matcha
}

public interface ITextToSpeechSynthesizer
{
    string Name { get; }

    Task<AudioData> SynthesizeAsync(string text, SpeechSynthesisOptions? options = null, CancellationToken cancellationToken = default);

    Task<string> SynthesizeToFileAsync(string text, string outputPath, SpeechSynthesisOptions? options = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AudioChunk> SynthesizeStreamingAsync(string text, SpeechSynthesisOptions? options = null, CancellationToken cancellationToken = default);
}

public sealed class WakeWordDetectedEventArgs(string keyword, DateTimeOffset detectedAt) : EventArgs
{
    public string Keyword { get; } = keyword;

    public DateTimeOffset DetectedAt { get; } = detectedAt;
}

public interface IWakeWordDetector : IAsyncDisposable
{
    event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

