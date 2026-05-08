using Microsoft.Extensions.Logging;
using SherpaOnnx;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public sealed class SherpaOnnxTextToSpeechSynthesizer : ITextToSpeechSynthesizer, IDisposable
{
    private readonly SherpaOnnxTtsOptions _options;
    private readonly ILogger<SherpaOnnxTextToSpeechSynthesizer> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SpeechSynthesisModelKind, Lazy<OfflineTts>> _ttsCache = new();
    private bool _disposed;

    public SherpaOnnxTextToSpeechSynthesizer(
        SherpaOnnxTtsOptions options,
        ILogger<SherpaOnnxTextToSpeechSynthesizer> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public string Name => "SherpaOnnx.OfflineTts";

    public Task<string> SynthesizeToFileAsync(string text, string outputPath, SpeechSynthesisOptions? options = null, CancellationToken cancellationToken = default)
        => SynthesizeToFileInternalAsync(text, outputPath, options, cancellationToken);

    public async Task<AudioData> SynthesizeAsync(string text, SpeechSynthesisOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var generated = GetTts(options?.ModelKind).Value.Generate(text, options?.Speed ?? 1.0f, options?.SpeakerId ?? 0);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new AudioData(generated.Samples, new AudioFormat(generated.SampleRate, 1));
        }
        finally
        {
            generated.Dispose();
        }
    }

    public async Task<string> SynthesizeToFileInternalAsync(string text, string outputPath, SpeechSynthesisOptions? options, CancellationToken cancellationToken)
    {
        var generated = GetTts(options?.ModelKind).Value.Generate(text, options?.Speed ?? 1.0f, options?.SpeakerId ?? 0);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            generated.SaveToWaveFile(outputPath);
            return await Task.FromResult(outputPath);
        }
        finally
        {
            generated.Dispose();
        }
    }

    public async IAsyncEnumerable<AudioChunk> SynthesizeStreamingAsync(
        string text,
        SpeechSynthesisOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var audio = await SynthesizeAsync(text, options, cancellationToken);
        await foreach (var chunk in audio.ToChunks(options?.StreamChunkSamples ?? 4096, cancellationToken))
        {
            yield return chunk;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var tts in _ttsCache.Values)
        {
            if (tts.IsValueCreated)
            {
                tts.Value.Dispose();
            }
        }
    }

    private Lazy<OfflineTts> GetTts(SpeechSynthesisModelKind? requestedKind)
    {
        var kind = requestedKind ?? _options.ModelKind;
        return _ttsCache.GetOrAdd(kind, static (kind, state) => new Lazy<OfflineTts>(
            () => state.CreateTts(kind),
            LazyThreadSafetyMode.ExecutionAndPublication),
            this);
    }

    private OfflineTts CreateTts(SpeechSynthesisModelKind modelKind)
    {
        _logger.LogInformation("Loading SherpaOnnx TTS model from {ModelPath} using {ModelKind}.", _options.ModelPath, modelKind);

        var modelConfig = new OfflineTtsModelConfig
        {
            Provider = _options.Provider,
            NumThreads = _options.Threads
        };

        switch (modelKind)
        {
            case SpeechSynthesisModelKind.Vits:
                modelConfig.Vits = new OfflineTtsVitsModelConfig
                {
                    Model = _options.ModelPath,
                    Tokens = _options.TokensPath,
                    Lexicon = _options.LexiconPath ?? string.Empty,
                    DataDir = _options.DataDirectory ?? string.Empty,
                    DictDir = _options.DictDirectory ?? string.Empty,
                    NoiseScale = _options.NoiseScale,
                    NoiseScaleW = _options.NoiseScaleW,
                    LengthScale = _options.LengthScale
                };
                break;

            case SpeechSynthesisModelKind.Matcha:
                modelConfig.Matcha = new OfflineTtsMatchaModelConfig
                {
                    AcousticModel = _options.ModelPath,
                    Vocoder = ResolveVocoderPath(_options.ModelPath, _options.VocoderPath),
                    Tokens = _options.TokensPath,
                    Lexicon = _options.LexiconPath ?? string.Empty,
                    DataDir = _options.DataDirectory ?? string.Empty,
                    DictDir = _options.DictDirectory ?? string.Empty,
                    NoiseScale = _options.NoiseScale,
                    LengthScale = _options.LengthScale
                };
                break;

            default:
                throw new NotSupportedException($"Unsupported TTS model kind: {modelKind}");
        }

        return new OfflineTts(new OfflineTtsConfig
        {
            MaxNumSentences = 1,
            RuleFars = _options.RuleFars ?? string.Empty,
            RuleFsts = ResolveRuleFsts(_options.ModelPath, _options.RuleFsts),
            Model = modelConfig
        });
    }

    private static string ResolveVocoderPath(string modelPath, string? vocoderPath)
    {
        if (!string.IsNullOrWhiteSpace(vocoderPath))
        {
            return vocoderPath;
        }

        string? modelDirectory = Path.GetDirectoryName(modelPath);
        if (!string.IsNullOrWhiteSpace(modelDirectory))
        {
            return Path.Combine(modelDirectory, "vocos-16khz-univ.onnx");
        }

        throw new InvalidOperationException(
            "SherpaOnnx Matcha TTS requires a vocoder file. Set VocoderPath explicitly or place vocos-16khz-univ.onnx next to the acoustic model.");
    }

    private static string ResolveRuleFsts(string modelPath, string? ruleFsts)
    {
        if (!string.IsNullOrWhiteSpace(ruleFsts))
        {
            return ruleFsts;
        }

        string? modelDirectory = Path.GetDirectoryName(modelPath);
        if (string.IsNullOrWhiteSpace(modelDirectory))
        {
            return string.Empty;
        }

        string[] preferredNames =
        [
            "phone-zh.fst",
            "date-zh.fst",
            "number-zh.fst"
        ];

        string[] resolved = preferredNames
            .Select(name => Path.Combine(modelDirectory, name))
            .Where(File.Exists)
            .ToArray();

        return resolved.Length > 0 ? string.Join(",", resolved) : string.Empty;
    }
}
