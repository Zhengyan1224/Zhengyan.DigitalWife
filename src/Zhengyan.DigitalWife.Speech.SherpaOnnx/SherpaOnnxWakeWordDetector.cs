using Microsoft.Extensions.Logging;
using SherpaOnnx;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Speech.SherpaOnnx;

public sealed class SherpaOnnxWakeWordDetector : IWakeWordDetector
{
    private readonly SherpaOnnxWakeWordOptions _options;
    private readonly IAudioSource _audioSource;
    private readonly ILogger<SherpaOnnxWakeWordDetector> _logger;
    private readonly KeywordSpotter _spotter;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public SherpaOnnxWakeWordDetector(
        SherpaOnnxWakeWordOptions options,
        IAudioSource audioSource,
        ILogger<SherpaOnnxWakeWordDetector> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audioSource = audioSource ?? throw new ArgumentNullException(nameof(audioSource));
        _logger = logger;
        ValidateOptions(_options);
        _spotter = new KeywordSpotter(BuildConfig(options));
    }

    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), _loopCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_loopCts is null || _loopTask is null)
        {
            return;
        }

        _loopCts.Cancel();
        try
        {
            await _loopTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _loopCts.Dispose();
            _loopCts = null;
            _loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _spotter.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var stream = _spotter.CreateStream();

        await foreach (var chunk in _audioSource.CaptureAsync(_options.CaptureOptions, cancellationToken))
        {
            var audio = new AudioData(chunk.Samples.ToArray(), chunk.Format).ToMono().Resample(_options.SampleRate);
            stream.AcceptWaveform(_options.SampleRate, audio.Samples);

            while (_spotter.IsReady(stream))
            {
                _spotter.Decode(stream);
                var result = _spotter.GetResult(stream);
                if (string.IsNullOrWhiteSpace(result.Keyword))
                {
                    continue;
                }

                _logger.LogInformation("Wake word detected: {Keyword}", result.Keyword);
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs(result.Keyword, DateTimeOffset.UtcNow));
                _spotter.Reset(stream);
            }
        }
    }

    private static KeywordSpotterConfig BuildConfig(SherpaOnnxWakeWordOptions options)
    {
        return new KeywordSpotterConfig
        {
            FeatConfig = new FeatureConfig
            {
                SampleRate = options.SampleRate,
                FeatureDim = options.FeatureDim
            },
            KeywordsFile = options.KeywordsFile,
            KeywordsScore = options.KeywordsScore,
            KeywordsThreshold = options.KeywordsThreshold,
            NumTrailingBlanks = options.NumTrailingBlanks,
            ModelConfig = new OnlineModelConfig
            {
                ModelType = "transducer",
                Tokens = options.TokensPath,
                Provider = options.Provider,
                NumThreads = options.Threads,
                Transducer = new OnlineTransducerModelConfig
                {
                    Encoder = options.EncoderPath,
                    Decoder = options.DecoderPath,
                    Joiner = options.JoinerPath
                }
            }
        };
    }

    private static void ValidateOptions(SherpaOnnxWakeWordOptions options)
    {
        ValidateRequiredFile(options.TokensPath, nameof(options.TokensPath));
        ValidateRequiredFile(options.EncoderPath, nameof(options.EncoderPath));
        ValidateRequiredFile(options.DecoderPath, nameof(options.DecoderPath));
        ValidateRequiredFile(options.JoinerPath, nameof(options.JoinerPath));
        ValidateRequiredFile(options.KeywordsFile, nameof(options.KeywordsFile));
    }

    private static void ValidateRequiredFile(string path, string optionName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{optionName} is required.", optionName);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required wake-word file was not found for {optionName}: {path}", path);
        }
    }
}

