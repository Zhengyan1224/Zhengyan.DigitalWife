using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public sealed class SherpaOnnxSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    private readonly SherpaOnnxRecognizerOptions _options;
    private readonly ILogger<SherpaOnnxSpeechRecognizer> _logger;
    private readonly Lazy<OfflineRecognizer?> _offlineRecognizer;
    private readonly Lazy<OnlineRecognizer?> _onlineRecognizer;
    private bool _disposed;

    public SherpaOnnxSpeechRecognizer(
        SherpaOnnxRecognizerOptions options,
        ILogger<SherpaOnnxSpeechRecognizer> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _offlineRecognizer = new Lazy<OfflineRecognizer?>(CreateOfflineRecognizer, LazyThreadSafetyMode.ExecutionAndPublication);
        _onlineRecognizer = new Lazy<OnlineRecognizer?>(CreateOnlineRecognizer, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name => $"SherpaOnnx:{_options.ModelKind}";

    public Task<SpeechRecognitionResult> RecognizeFileAsync(
        string path,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
        => RecognizeFileInternalAsync(path, options, cancellationToken);

    public async Task<SpeechRecognitionResult> RecognizeAsync(
        AudioData audio,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOfflineModel(_options.ModelKind))
        {
            return await RecognizeWithStreamingFallbackAsync(audio, options, cancellationToken);
        }

        var recognizer = _offlineRecognizer.Value ?? throw new InvalidOperationException("Offline recognizer is not configured.");
        var normalized = audio.ToMono().Resample(_options.SampleRate);
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(_options.SampleRate, normalized.Samples);
        recognizer.Decode(stream);

        var result = stream.Result;
        var segments = CreateSegments(result.Text, result.Timestamps);

        return new SpeechRecognitionResult
        {
            Text = result.Text?.Trim() ?? string.Empty,
            Language = options?.Language ?? _options.Language,
            Segments = segments
        };
    }

    public IStreamingSpeechRecognitionSession CreateStreamingSession(StreamingSpeechRecognitionOptions? options = null)
    {
        if (!IsOnlineModel(_options.ModelKind))
        {
            return new BufferedSpeechRecognitionSession(this, options ?? new StreamingSpeechRecognitionOptions());
        }

        var recognizer = _onlineRecognizer.Value ?? throw new InvalidOperationException("Online recognizer is not configured.");
        return new SherpaOnnxStreamingRecognitionSession(recognizer, _options, options ?? new StreamingSpeechRecognitionOptions(), _logger);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_offlineRecognizer.IsValueCreated)
        {
            _offlineRecognizer.Value?.Dispose();
        }

        if (_onlineRecognizer.IsValueCreated)
        {
            _onlineRecognizer.Value?.Dispose();
        }
    }

    private async Task<SpeechRecognitionResult> RecognizeFileInternalAsync(
        string path,
        SpeechRecognitionOptions? options,
        CancellationToken cancellationToken)
    {
        var audio = await WaveFile.ReadAsync(path, cancellationToken);
        return await RecognizeAsync(audio, options, cancellationToken);
    }

    private async Task<SpeechRecognitionResult> RecognizeWithStreamingFallbackAsync(
        AudioData audio,
        SpeechRecognitionOptions? options,
        CancellationToken cancellationToken)
    {
        await using var session = CreateStreamingSession(options is StreamingSpeechRecognitionOptions streamingOptions
            ? streamingOptions
            : new StreamingSpeechRecognitionOptions
            {
                Language = options?.Language,
                EnableTimestamps = options?.EnableTimestamps ?? false,
                TranslateToEnglish = options?.TranslateToEnglish ?? false
            });

        var updatesTask = Task.Run(async () =>
        {
            SpeechRecognitionUpdate? final = null;
            await foreach (var update in session.GetUpdatesAsync(cancellationToken))
            {
                final = update;
            }

            return final;
        }, cancellationToken);

        await foreach (var chunk in audio.ToMono().Resample(_options.SampleRate).ToChunks(cancellationToken: cancellationToken))
        {
            await session.WriteAsync(chunk, cancellationToken);
        }

        await session.CompleteAsync(cancellationToken);
        var last = await updatesTask;

        return new SpeechRecognitionResult
        {
            Text = last?.Text ?? string.Empty,
            Language = options?.Language ?? _options.Language,
            Segments = last?.Segments ?? []
        };
    }

    private OfflineRecognizer? CreateOfflineRecognizer()
    {
        if (!IsOfflineModel(_options.ModelKind))
        {
            return null;
        }

        _logger.LogInformation("Creating SherpaOnnx offline recognizer with model kind {ModelKind}.", _options.ModelKind);
        return new OfflineRecognizer(BuildOfflineConfig());
    }

    private OnlineRecognizer? CreateOnlineRecognizer()
    {
        if (!IsOnlineModel(_options.ModelKind))
        {
            return null;
        }

        _logger.LogInformation("Creating SherpaOnnx online recognizer with model kind {ModelKind}.", _options.ModelKind);
        return new OnlineRecognizer(BuildOnlineConfig());
    }

    private OfflineRecognizerConfig BuildOfflineConfig()
    {
        var modelConfig = new OfflineModelConfig
        {
            Tokens = _options.TokensPath,
            Provider = _options.Provider,
            NumThreads = _options.Threads,
            ModelType = GetModelType(_options.ModelKind)
        };

        switch (_options.ModelKind)
        {
            case SherpaOnnxRecognizerModelKind.OfflineWhisper:
                modelConfig.Whisper = new OfflineWhisperModelConfig
                {
                    Encoder = Required(_options.EncoderPath),
                    Decoder = Required(_options.DecoderPath),
                    Language = _options.Language,
                    Task = "transcribe",
                    TailPaddings = 0,
                    EnableSegmentTimestamps = 1,
                    EnableTokenTimestamps = 1
                };
                break;

            case SherpaOnnxRecognizerModelKind.OfflineParaformer:
                modelConfig.Paraformer = new OfflineParaformerModelConfig
                {
                    Model = Required(_options.ModelPath)
                };
                break;

            case SherpaOnnxRecognizerModelKind.OfflineTransducer:
                modelConfig.Transducer = new OfflineTransducerModelConfig
                {
                    Encoder = Required(_options.EncoderPath),
                    Decoder = Required(_options.DecoderPath),
                    Joiner = Required(_options.JoinerPath)
                };
                break;

            case SherpaOnnxRecognizerModelKind.OfflineZipformerCtc:
                modelConfig.ZipformerCtc = new OfflineZipformerCtcModelConfig
                {
                    Model = Required(_options.ModelPath)
                };
                break;

            case SherpaOnnxRecognizerModelKind.OfflineWenetCtc:
                modelConfig.WenetCtc = new OfflineWenetCtcModelConfig
                {
                    Model = Required(_options.ModelPath)
                };
                break;

            default:
                throw new NotSupportedException($"Unsupported offline Sherpa model kind: {_options.ModelKind}");
        }

        return new OfflineRecognizerConfig
        {
            ModelConfig = modelConfig,
            FeatConfig = new FeatureConfig
            {
                SampleRate = _options.SampleRate,
                FeatureDim = _options.FeatureDim
            },
            DecodingMethod = _options.DecodingMethod,
            HotwordsFile = _options.HotwordsFile ?? string.Empty,
            HotwordsScore = _options.HotwordsScore
        };
    }

    private OnlineRecognizerConfig BuildOnlineConfig()
    {
        var modelConfig = new OnlineModelConfig
        {
            Tokens = _options.TokensPath,
            Provider = _options.Provider,
            NumThreads = _options.Threads,
            ModelType = GetModelType(_options.ModelKind)
        };

        switch (_options.ModelKind)
        {
            case SherpaOnnxRecognizerModelKind.OnlineTransducer:
                modelConfig.Transducer = new OnlineTransducerModelConfig
                {
                    Encoder = Required(_options.EncoderPath),
                    Decoder = Required(_options.DecoderPath),
                    Joiner = Required(_options.JoinerPath)
                };
                break;

            case SherpaOnnxRecognizerModelKind.OnlineParaformer:
                modelConfig.Paraformer = new OnlineParaformerModelConfig
                {
                    Encoder = Required(_options.EncoderPath),
                    Decoder = Required(_options.DecoderPath)
                };
                break;

            case SherpaOnnxRecognizerModelKind.OnlineZipformer2Ctc:
                modelConfig.Zipformer2Ctc = new OnlineZipformer2CtcModelConfig
                {
                    Model = Required(_options.ModelPath)
                };
                break;

            default:
                throw new NotSupportedException($"Unsupported online Sherpa model kind: {_options.ModelKind}");
        }

        return new OnlineRecognizerConfig
        {
            ModelConfig = modelConfig,
            FeatConfig = new FeatureConfig
            {
                SampleRate = _options.SampleRate,
                FeatureDim = _options.FeatureDim
            },
            DecodingMethod = _options.DecodingMethod,
            HotwordsFile = _options.HotwordsFile ?? string.Empty,
            HotwordsScore = _options.HotwordsScore,
            EnableEndpoint = 1
        };
    }

    private static bool IsOfflineModel(SherpaOnnxRecognizerModelKind kind)
        => kind is SherpaOnnxRecognizerModelKind.OfflineWhisper
            or SherpaOnnxRecognizerModelKind.OfflineParaformer
            or SherpaOnnxRecognizerModelKind.OfflineTransducer
            or SherpaOnnxRecognizerModelKind.OfflineZipformerCtc
            or SherpaOnnxRecognizerModelKind.OfflineWenetCtc;

    private static bool IsOnlineModel(SherpaOnnxRecognizerModelKind kind)
        => kind is SherpaOnnxRecognizerModelKind.OnlineTransducer
            or SherpaOnnxRecognizerModelKind.OnlineParaformer
            or SherpaOnnxRecognizerModelKind.OnlineZipformer2Ctc;

    private static string GetModelType(SherpaOnnxRecognizerModelKind kind) => kind switch
    {
        SherpaOnnxRecognizerModelKind.OfflineWhisper => "whisper",
        SherpaOnnxRecognizerModelKind.OfflineParaformer => "paraformer",
        SherpaOnnxRecognizerModelKind.OfflineTransducer => "transducer",
        SherpaOnnxRecognizerModelKind.OfflineZipformerCtc => "zipformer_ctc",
        SherpaOnnxRecognizerModelKind.OfflineWenetCtc => "wenet_ctc",
        SherpaOnnxRecognizerModelKind.OnlineTransducer => "transducer",
        SherpaOnnxRecognizerModelKind.OnlineParaformer => "paraformer",
        SherpaOnnxRecognizerModelKind.OnlineZipformer2Ctc => "zipformer2_ctc",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static IReadOnlyList<SpeechRecognitionSegment> CreateSegments(string? text, float[]? timestamps)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (timestamps is null || timestamps.Length < 2)
        {
            return [new SpeechRecognitionSegment(text.Trim(), TimeSpan.Zero, TimeSpan.Zero)];
        }

        return [new SpeechRecognitionSegment(text.Trim(), TimeSpan.FromSeconds(timestamps.First()), TimeSpan.FromSeconds(timestamps.Last()))];
    }

    private static string Required(string? value)
        => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException("Model path is required.");
}

internal sealed class SherpaOnnxStreamingRecognitionSession : IStreamingSpeechRecognitionSession
{
    private readonly OnlineRecognizer _recognizer;
    private readonly SherpaOnnxRecognizerOptions _options;
    private readonly StreamingSpeechRecognitionOptions _sessionOptions;
    private readonly ILogger _logger;
    private readonly OnlineStream _stream;
    private readonly Channel<SpeechRecognitionUpdate> _updates = Channel.CreateUnbounded<SpeechRecognitionUpdate>();
    private string _lastText = string.Empty;

    public SherpaOnnxStreamingRecognitionSession(
        OnlineRecognizer recognizer,
        SherpaOnnxRecognizerOptions options,
        StreamingSpeechRecognitionOptions sessionOptions,
        ILogger logger)
    {
        _recognizer = recognizer;
        _options = options;
        _sessionOptions = sessionOptions;
        _logger = logger;
        _stream = _recognizer.CreateStream();
    }

    public async ValueTask WriteAsync(AudioChunk chunk, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeChunk(chunk, _options.SampleRate);
        _stream.AcceptWaveform(_options.SampleRate, normalized);

        while (_recognizer.IsReady(_stream))
        {
            _recognizer.Decode(_stream);
            var result = _recognizer.GetResult(_stream);
            if (string.Equals(result.Text, _lastText, StringComparison.Ordinal))
            {
                continue;
            }

            _lastText = result.Text;
            await _updates.Writer.WriteAsync(new SpeechRecognitionUpdate
            {
                Text = result.Text,
                IsFinal = false,
                Offset = chunk.Offset + chunk.Duration,
                Segments = string.IsNullOrWhiteSpace(result.Text)
                    ? []
                    : [new SpeechRecognitionSegment(result.Text, TimeSpan.Zero, TimeSpan.Zero)]
            }, cancellationToken);
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        _stream.InputFinished();

        while (_recognizer.IsReady(_stream))
        {
            _recognizer.Decode(_stream);
        }

        var finalResult = _recognizer.GetResult(_stream);
        var finalText = !string.IsNullOrWhiteSpace(finalResult.Text) ? finalResult.Text : _lastText;
        await _updates.Writer.WriteAsync(new SpeechRecognitionUpdate
        {
            Text = finalText,
            IsFinal = true,
            Offset = TimeSpan.Zero,
            Segments = string.IsNullOrWhiteSpace(finalText)
                ? []
                : [new SpeechRecognitionSegment(finalText, TimeSpan.Zero, TimeSpan.Zero)]
        }, cancellationToken);

        _updates.Writer.TryComplete();
        _recognizer.Reset(_stream);
    }

    public IAsyncEnumerable<SpeechRecognitionUpdate> GetUpdatesAsync(CancellationToken cancellationToken = default)
        => _updates.Reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _updates.Writer.TryComplete();
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }

    private static float[] NormalizeChunk(AudioChunk chunk, int sampleRate)
    {
        var audio = new AudioData(chunk.Samples.ToArray(), chunk.Format).ToMono().Resample(sampleRate);
        return audio.Samples;
    }
}

internal sealed class BufferedSpeechRecognitionSession : IStreamingSpeechRecognitionSession
{
    private readonly SherpaOnnxSpeechRecognizer _recognizer;
    private readonly StreamingSpeechRecognitionOptions _options;
    private readonly List<float> _samples = [];
    private AudioFormat? _format;
    private readonly Channel<SpeechRecognitionUpdate> _updates = Channel.CreateUnbounded<SpeechRecognitionUpdate>();

    public BufferedSpeechRecognitionSession(
        SherpaOnnxSpeechRecognizer recognizer,
        StreamingSpeechRecognitionOptions options)
    {
        _recognizer = recognizer;
        _options = options;
    }

    public ValueTask WriteAsync(AudioChunk chunk, CancellationToken cancellationToken = default)
    {
        _format ??= chunk.Format;
        _samples.AddRange(chunk.Samples.ToArray());
        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_format is not null)
        {
            var result = await _recognizer.RecognizeAsync(new AudioData(_samples.ToArray(), _format), _options, cancellationToken);
            await _updates.Writer.WriteAsync(new SpeechRecognitionUpdate
            {
                Text = result.Text,
                IsFinal = true,
                Offset = TimeSpan.Zero,
                Segments = result.Segments
            }, cancellationToken);
        }

        _updates.Writer.TryComplete();
    }

    public IAsyncEnumerable<SpeechRecognitionUpdate> GetUpdatesAsync(CancellationToken cancellationToken = default)
        => _updates.Reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _updates.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

