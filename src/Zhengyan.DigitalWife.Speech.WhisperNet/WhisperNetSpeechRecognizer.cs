using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Speech;
using Whisper.net;

namespace Zhengyan.DigitalWife.Speech.WhisperNet;

public sealed class WhisperNetSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    private readonly WhisperNetRecognizerOptions _options;
    private readonly ILogger<WhisperNetSpeechRecognizer> _logger;
    private readonly Lazy<WhisperFactory> _factory;
    private bool _disposed;

    public WhisperNetSpeechRecognizer(
        WhisperNetRecognizerOptions options,
        ILogger<WhisperNetSpeechRecognizer> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _factory = new Lazy<WhisperFactory>(CreateFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name => "Whisper.net";

    public async Task<SpeechRecognitionResult> RecognizeAsync(
        AudioData audio,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audio);

        var normalized = audio.ToMono().Resample(_options.SampleRate);
        var builder = CreateBuilder(options);
        using var processor = builder.Build();

        var segments = new List<SpeechRecognitionSegment>();
        var text = new StringBuilder();

        await foreach (var segment in processor.ProcessAsync(normalized.Samples, cancellationToken))
        {
            var chunkText = segment.Text.Trim();
            if (string.IsNullOrWhiteSpace(chunkText))
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(chunkText);
            segments.Add(new SpeechRecognitionSegment(chunkText, segment.Start, segment.End));
        }

        return new SpeechRecognitionResult
        {
            Text = text.ToString().Trim(),
            Language = options?.Language ?? _options.Language,
            Segments = segments
        };
    }

    public async Task<SpeechRecognitionResult> RecognizeFileAsync(
        string path,
        SpeechRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var audio = await WaveFile.ReadAsync(path, cancellationToken);
        return await RecognizeAsync(audio, options, cancellationToken);
    }

    public IStreamingSpeechRecognitionSession CreateStreamingSession(StreamingSpeechRecognitionOptions? options = null)
        => new WhisperNetStreamingRecognitionSession(this, options ?? new StreamingSpeechRecognitionOptions(), _logger);

    public WhisperNetRuntimeDiagnostics GetRuntimeDiagnostics()
    {
        string? initializationError = null;

        try
        {
            _ = _factory.Value;
        }
        catch (Exception ex)
        {
            initializationError = ex.Message;
        }

        string root = AppContext.BaseDirectory;
        return new WhisperNetRuntimeDiagnostics
        {
            RequestedUseGpu = _options.UseGpu,
            NativeSearchRoot = root,
            FoundNativeFiles = FindNativeRuntimeFiles(root),
            LoadedRuntimeLibrary = ReadRuntimeOptionsProperty("LoadedLibrary"),
            RuntimeLibraryOrder = ReadRuntimeLibraryOrder(),
            InitializationError = initializationError
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_factory.IsValueCreated)
        {
            _factory.Value.Dispose();
        }
    }

    internal WhisperProcessorBuilder CreateBuilder(SpeechRecognitionOptions? options)
    {
        var builder = _factory.Value.CreateBuilder()
            .WithThreads(_options.Threads);

        var language = options?.Language ?? _options.Language;
        if (string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithLanguageDetection();
        }
        else
        {
            builder = builder.WithLanguage(language);
        }

        if ((options?.TranslateToEnglish ?? _options.TranslateToEnglish) == true)
        {
            builder = builder.WithTranslate();
        }

        if (options?.EnableTimestamps == true)
        {
            builder = builder.WithTokenTimestamps();
        }

        return builder;
    }

    private WhisperFactory CreateFactory()
    {
        _logger.LogInformation("Loading Whisper model from {ModelPath}.", _options.ModelPath);
        var factoryOptions = new WhisperFactoryOptions
        {
            UseGpu = _options.UseGpu
        };

        return WhisperFactory.FromPath(_options.ModelPath, factoryOptions);
    }

    private static string? ReadRuntimeOptionsProperty(string propertyName)
    {
        Type? runtimeOptionsType = typeof(WhisperFactory).Assembly.GetType("Whisper.net.LibraryLoader.RuntimeOptions");
        PropertyInfo? property = runtimeOptionsType?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        object? value = property?.GetValue(null);
        return value?.ToString();
    }

    private static IReadOnlyList<string> ReadRuntimeLibraryOrder()
    {
        Type? runtimeOptionsType = typeof(WhisperFactory).Assembly.GetType("Whisper.net.LibraryLoader.RuntimeOptions");
        PropertyInfo? property = runtimeOptionsType?.GetProperty("RuntimeLibraryOrder", BindingFlags.Public | BindingFlags.Static);
        object? value = property?.GetValue(null);
        if (value is System.Collections.IEnumerable enumerable)
        {
            List<string> items = [];
            foreach (object? item in enumerable)
            {
                if (item is not null)
                {
                    items.Add(item.ToString() ?? string.Empty);
                }
            }

            return items;
        }

        return [];
    }

    private static IReadOnlyList<string> FindNativeRuntimeFiles(string root)
    {
        string[] interestingNames =
        [
            "ggml-cpu-whisper.dll",
            "ggml-cpu-whisper.so",
            "libggml-cpu-whisper.dylib",
            "ggml-cuda-whisper.dll",
            "ggml-cuda-whisper.so",
            "libggml-cuda-whisper.dylib",
            "ggml-vulkan-whisper.dll",
            "ggml-vulkan-whisper.so",
            "libggml-vulkan-whisper.dylib",
            "ggml-openvino-whisper.dll",
            "ggml-openvino-whisper.so",
            "libggml-openvino-whisper.dylib"
        ];

        List<string> found = [];
        foreach (string fileName in interestingNames)
        {
            string direct = Path.Combine(root, fileName);
            if (File.Exists(direct))
            {
                found.Add(direct);
            }
        }

        string runtimesDirectory = Path.Combine(root, "runtimes");
        if (!Directory.Exists(runtimesDirectory))
        {
            return found;
        }

        foreach (string fileName in interestingNames)
        {
            foreach (string path in Directory.EnumerateFiles(runtimesDirectory, fileName, SearchOption.AllDirectories))
            {
                if (!found.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(path);
                }
            }
        }

        return found;
    }
}

internal sealed class WhisperNetStreamingRecognitionSession : IStreamingSpeechRecognitionSession
{
    private readonly WhisperNetSpeechRecognizer _recognizer;
    private readonly StreamingSpeechRecognitionOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<AudioChunk> _audioChannel = Channel.CreateUnbounded<AudioChunk>();
    private readonly Channel<SpeechRecognitionUpdate> _updateChannel = Channel.CreateUnbounded<SpeechRecognitionUpdate>();
    private readonly Task _backgroundTask;
    private readonly List<float> _buffer = [];
    private string _lastPublishedText = string.Empty;

    public WhisperNetStreamingRecognitionSession(
        WhisperNetSpeechRecognizer recognizer,
        StreamingSpeechRecognitionOptions options,
        ILogger logger)
    {
        _recognizer = recognizer;
        _options = options;
        _logger = logger;
        _backgroundTask = Task.Run(ProcessAsync);
    }

    public ValueTask WriteAsync(AudioChunk chunk, CancellationToken cancellationToken = default)
        => _audioChannel.Writer.WriteAsync(chunk, cancellationToken);

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        _audioChannel.Writer.TryComplete();
        await _backgroundTask.WaitAsync(cancellationToken);
    }

    public IAsyncEnumerable<SpeechRecognitionUpdate> GetUpdatesAsync(CancellationToken cancellationToken = default)
        => _updateChannel.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _audioChannel.Writer.TryComplete();
        _updateChannel.Writer.TryComplete();
        await _backgroundTask;
    }

    private async Task ProcessAsync()
    {
        AudioFormat? format = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await foreach (var chunk in _audioChannel.Reader.ReadAllAsync())
            {
                format ??= chunk.Format;
                _buffer.AddRange(chunk.Samples.ToArray());

                if (stopwatch.Elapsed < _options.PartialResultInterval && !chunk.IsFinal)
                {
                    continue;
                }

                stopwatch.Restart();
                await PublishRecognitionAsync(format, isFinal: false);
            }

            if (format is not null)
            {
                await PublishRecognitionAsync(format, isFinal: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper streaming recognition session failed.");
            _updateChannel.Writer.TryComplete(ex);
            return;
        }

        _updateChannel.Writer.TryComplete();
    }

    private async Task PublishRecognitionAsync(AudioFormat format, bool isFinal)
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        var audio = new AudioData(_buffer.ToArray(), format);
        var result = await _recognizer.RecognizeAsync(audio, _options);
        if (string.Equals(result.Text, _lastPublishedText, StringComparison.Ordinal))
        {
            if (isFinal)
            {
                await _updateChannel.Writer.WriteAsync(new SpeechRecognitionUpdate
                {
                    Text = result.Text,
                    IsFinal = true,
                    Offset = audio.Duration,
                    Segments = result.Segments
                });
            }

            return;
        }

        _lastPublishedText = result.Text;
        await _updateChannel.Writer.WriteAsync(new SpeechRecognitionUpdate
        {
            Text = result.Text,
            IsFinal = isFinal,
            Offset = audio.Duration,
            Segments = result.Segments
        });
    }
}

