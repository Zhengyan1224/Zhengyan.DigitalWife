using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Assistant;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.OpenAL;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.Assistant.Conversation;
using Zhengyan.DigitalWife.Samples.AssistantConsole;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

var cliOptions = DemoCliOptions.Parse(args);
var appBasePath = AppContext.BaseDirectory;

var builder = Host.CreateApplicationBuilder(args);
builder.Environment.ContentRootPath = appBasePath;

builder.Configuration
    .SetBasePath(appBasePath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "DIGITALWIFE_")
    .AddEnvironmentVariables(prefix: "SPEECHBRIDGE_");

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
    options.SingleLine = true;
});

var demoOptions = builder.Configuration.GetSection("Demo").Get<DemoOptions>()
    ?? throw new InvalidOperationException("Missing Demo configuration.");
demoOptions = new SamplePathResolver(appBasePath).Resolve(demoOptions);

var runtimeAudioOptions = new PortAudioRuntimeOptions
{
    InputDeviceIndex = cliOptions.InputDeviceIndex ?? demoOptions.Audio.InputDeviceIndex,
    OutputDeviceIndex = cliOptions.OutputDeviceIndex ?? demoOptions.Audio.OutputDeviceIndex
};

builder.Services.AddDigitalWifeAssistantCore();
builder.Services.AddPortAudioInput(runtimeAudioOptions);
switch (demoOptions.Audio.PlaybackBackend)
{
    case AudioPlaybackBackend.PortAudio:
        builder.Services.AddPortAudioOutput(runtimeAudioOptions);
        break;

    case AudioPlaybackBackend.OpenAL:
        builder.Services.AddOpenAlAudioPlayer();
        break;

    default:
        throw new InvalidOperationException($"Unsupported audio playback backend: {demoOptions.Audio.PlaybackBackend}");
}
builder.Services.AddOpenAiCompatibleLlmClient(demoOptions.Llm);
builder.Services.AddSherpaOnnxTextToSpeech(demoOptions.Tts);

switch (demoOptions.RecognitionProvider.ToLowerInvariant())
{
    case "sherpa":
        builder.Services.AddSherpaOnnxSpeechRecognizer(demoOptions.SherpaRecognizer
            ?? throw new InvalidOperationException("Demo:SherpaRecognizer configuration is required when RecognitionProvider=sherpa."));
        if (demoOptions.WhisperRecognizer is not null)
        {
            builder.Services.AddWhisperNetSpeechRecognizer(demoOptions.WhisperRecognizer);
        }
        break;

    case "whisper":
        builder.Services.AddWhisperNetSpeechRecognizer(demoOptions.WhisperRecognizer
            ?? throw new InvalidOperationException("Demo:WhisperRecognizer configuration is required when RecognitionProvider=whisper."));
        if (demoOptions.SherpaRecognizer is not null)
        {
            builder.Services.AddSherpaOnnxSpeechRecognizer(demoOptions.SherpaRecognizer);
        }
        break;

    default:
        throw new InvalidOperationException($"Unsupported recognition provider: {demoOptions.RecognitionProvider}");
}

if (demoOptions.WakeWord is not null)
{
    builder.Services.AddSherpaOnnxWakeWordDetector(demoOptions.WakeWord);
}

builder.Services.AddSingleton(demoOptions);
builder.Services.AddSingleton(cliOptions);
builder.Services.AddHostedService<DemoHostedService>();

await builder.Build().RunAsync();

internal sealed class DemoHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DemoOptions _options;
    private readonly DemoCliOptions _cliOptions;
    private readonly ILogger<DemoHostedService> _logger;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly IHostApplicationLifetime _appLifetime;
    private IWakeWordDetector? _wakeWordDetector;

    public DemoHostedService(
        IServiceProvider serviceProvider,
        DemoOptions options,
        DemoCliOptions cliOptions,
        IHostApplicationLifetime appLifetime,
        ILogger<DemoHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _cliOptions = cliOptions;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Zhengyan.DigitalWife.Assistant demo starting. Recognition provider: {Provider}. Playback backend: {Backend}",
            _options.RecognitionProvider,
            _options.Audio.PlaybackBackend);

        if (_cliOptions.ListDevices)
        {
            ListDevices();
            _appLifetime.StopApplication();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_cliOptions.TranscribeFile))
        {
            await TranscribeFileAsync(_cliOptions.TranscribeFile, cancellationToken);
            _appLifetime.StopApplication();
            return;
        }

        _wakeWordDetector = _serviceProvider.GetService<IWakeWordDetector>();
        if (_wakeWordDetector is null || _cliOptions.RunOnce)
        {
            if (_wakeWordDetector is null)
            {
                _logger.LogWarning("Wake word detector is not configured. Running a single turn immediately.");
            }

            await RunTurnAsync(cancellationToken);
            _appLifetime.StopApplication();
            return;
        }

        _wakeWordDetector.WakeWordDetected += OnWakeWordDetected;
        await _wakeWordDetector.StartAsync(cancellationToken);
        _logger.LogInformation("Wake word detector started. Press Ctrl+C to exit.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_wakeWordDetector is null)
        {
            return;
        }

        _wakeWordDetector.WakeWordDetected -= OnWakeWordDetected;
        await _wakeWordDetector.StopAsync(cancellationToken);
        await _wakeWordDetector.DisposeAsync();
    }

    private void ListDevices()
    {
        var catalog = _serviceProvider.GetRequiredService<PortAudioDeviceCatalog>();
        _logger.LogInformation("Input devices:");
        foreach (var device in catalog.ListInputDevices())
        {
            _logger.LogInformation("  [{Index}] {Name} in={In} out={Out} defaultRate={Rate}",
                device.Index, device.Name, device.MaxInputChannels, device.MaxOutputChannels, device.DefaultSampleRate);
        }

        _logger.LogInformation("Output devices:");
        foreach (var device in catalog.ListOutputDevices())
        {
            _logger.LogInformation("  [{Index}] {Name} in={In} out={Out} defaultRate={Rate}",
                device.Index, device.Name, device.MaxInputChannels, device.MaxOutputChannels, device.DefaultSampleRate);
        }
    }

    private async Task TranscribeFileAsync(string path, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var recognizers = scope.ServiceProvider.GetServices<ISpeechRecognizer>().ToArray();
        if (recognizers.Length == 0)
        {
            throw new InvalidOperationException("No speech recognizers are registered.");
        }

        SpeechRecognitionResult? result = null;
        foreach (var recognizer in recognizers)
        {
            _logger.LogInformation("Transcribing with provider {Provider}.", recognizer.Name);
            result = await recognizer.RecognizeFileAsync(path, new SpeechRecognitionOptions
            {
                Language = "zh",
                EnableTimestamps = true
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                break;
            }
        }

        _logger.LogInformation("Transcription file: {Path}", path);
        _logger.LogInformation("Transcription text: {Text}", result?.Text ?? string.Empty);
    }

    private void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
    {
        _ = HandleWakeWordAsync(e);
    }

    private async Task HandleWakeWordAsync(WakeWordDetectedEventArgs e)
    {
        if (!await _turnGate.WaitAsync(0))
        {
            _logger.LogWarning("Wake word ignored because another turn is still running.");
            return;
        }

        try
        {
            _logger.LogInformation("Wake word event received: {Keyword}", e.Keyword);

            if (_wakeWordDetector is not null)
            {
                await _wakeWordDetector.StopAsync();
            }

            await RunTurnAsync(CancellationToken.None);

            if (_wakeWordDetector is not null)
            {
                await _wakeWordDetector.StartAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice assistant turn failed after wake word.");
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private async Task RunTurnAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<VoiceAssistantPipeline>();

        var capturePath = !string.IsNullOrWhiteSpace(_options.CapturedAudioDirectory)
            ? Path.Combine(_options.CapturedAudioDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}.wav")
            : null;

        var result = await pipeline.RunTurnAsync(new VoiceAssistantTurnOptions
        {
            SystemPrompt = _options.SystemPrompt,
            LlmOptions = new LlmRequestOptions
            {
                Model = _options.LlmModel
            },
            CaptureOptions = _options.Capture,
            RecognitionOptions = new SpeechRecognitionOptions
            {
                Language = "zh",
                EnableTimestamps = true
            },
            SynthesisOptions = new SpeechSynthesisOptions
            {
                ModelKind = _options.Tts.ModelKind,
                Speed = 1.0f
            },
            CapturedAudioPath = capturePath
        }, cancellationToken);

        _logger.LogInformation("User: {Text}", result.UserText);
        _logger.LogInformation("Assistant: {Text}", result.AssistantText);
    }
}

internal sealed record DemoCliOptions(
    bool ListDevices,
    bool RunOnce,
    int? InputDeviceIndex,
    int? OutputDeviceIndex,
    string? TranscribeFile)
{
    public static DemoCliOptions Parse(string[] args)
    {
        var listDevices = false;
        var runOnce = false;
        int? inputDevice = null;
        int? outputDevice = null;
        string? transcribeFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list-devices":
                    listDevices = true;
                    break;
                case "--run-once":
                    runOnce = true;
                    break;
                case "--input-device" when i + 1 < args.Length && int.TryParse(args[i + 1], out var input):
                    inputDevice = input;
                    i++;
                    break;
                case "--output-device" when i + 1 < args.Length && int.TryParse(args[i + 1], out var output):
                    outputDevice = output;
                    i++;
                    break;
                case "--transcribe-file" when i + 1 < args.Length:
                    transcribeFile = args[i + 1];
                    i++;
                    break;
            }
        }

        return new DemoCliOptions(listDevices, runOnce, inputDevice, outputDevice, transcribeFile);
    }
}

