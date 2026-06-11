using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeAsr : IDisposable
{
    private readonly string _projectDirectory;
    private readonly GameProjectAsrSettings _settings;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly Action<RuntimeEntity, RuntimeAsrScriptEvent> _dispatchScriptEvent;
    private readonly IAudioSource _audioSource;
    private readonly bool _microphoneInputAvailable;
    private readonly string _microphoneUnavailableReason;
    private readonly object _sync = new();
    private readonly Dictionary<string, CancellationTokenSource> _activeRequests = new(StringComparer.OrdinalIgnoreCase);
    private ISpeechRecognizer? _recognizer;
    private bool _disposed;

    internal RuntimeAsr(
        string projectDirectory,
        GameProjectAsrSettings settings,
        MainThreadDispatcher dispatcher,
        Action<RuntimeEntity, RuntimeAsrScriptEvent> dispatchScriptEvent,
        bool microphoneInputAvailable = true,
        string? microphoneUnavailableReason = null)
    {
        _projectDirectory = projectDirectory;
        _settings = settings;
        _dispatcher = dispatcher;
        _dispatchScriptEvent = dispatchScriptEvent;
        _microphoneInputAvailable = microphoneInputAvailable;
        _microphoneUnavailableReason = string.IsNullOrWhiteSpace(microphoneUnavailableReason)
            ? "No usable microphone input device is available."
            : microphoneUnavailableReason.Trim();
        _audioSource = new PortAudioMicrophoneSource(
            NullLogger<PortAudioMicrophoneSource>.Instance,
            new PortAudioRuntimeOptions
            {
                InputDeviceIndex = settings.InputDeviceIndex
            });
    }

    public bool Enabled => _settings.Enabled && _microphoneInputAvailable;

    public string Provider => _settings.Provider;

    public int? InputDeviceIndex => _settings.InputDeviceIndex;

    public bool MicrophoneInputAvailable => _microphoneInputAvailable;

    public string MicrophoneUnavailableReason => _microphoneInputAvailable ? string.Empty : _microphoneUnavailableReason;

    public float PartialResultIntervalSeconds => _settings.PartialResultIntervalSeconds;

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _activeRequests.Count != 0;
            }
        }
    }

    public void Preload()
    {
        if (_disposed || !_settings.Enabled || !_settings.PreloadOnSceneLoad)
        {
            return;
        }

        try
        {
            ISpeechRecognizer recognizer = GetRecognizer();
            int sampleRate = string.Equals(_settings.Provider, "whisper", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(8000, _settings.Whisper.SampleRate)
                : Math.Max(8000, _settings.Sherpa.SampleRate);
            int warmupSamples = Math.Max(sampleRate / 4, 1600);
            AudioData silence = new(new float[warmupSamples], new AudioFormat(sampleRate, 1));

            _ = recognizer.RecognizeAsync(
                silence,
                new SpeechRecognitionOptions
                {
                    Language = ResolveLanguage(),
                    TranslateToEnglish = ResolveTranslateToEnglish()
                }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ASR preload failed: {ex.Message}");
        }
    }

    public string StartStreamingRecognition(
        RuntimeEntity callbackTarget,
        string? requestId = null,
        string? onPartialCallback = "asr_partial",
        string? onCompletedCallback = "asr_completed",
        string? onErrorCallback = "asr_error")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);
        EnsureEnabled();

        StopAllStreamingRecognitions();

        string resolvedRequestId = ResolveRequestId(requestId);
        Console.WriteLine(
            $"[GamePlayer] ASR streaming start request={resolvedRequestId}, target={callbackTarget.Name}, " +
            $"device={_settings.InputDeviceIndex?.ToString() ?? "default"}, provider={_settings.Provider}");
        CancellationTokenSource cts = new();
        lock (_sync)
        {
            _activeRequests[resolvedRequestId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunStreamingRecognitionAsync(
                    callbackTarget,
                    resolvedRequestId,
                    onPartialCallback,
                    onCompletedCallback,
                    cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested || _disposed)
            {
                Console.WriteLine($"[GamePlayer] ASR streaming canceled request={resolvedRequestId}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GamePlayer] ASR streaming failed request={resolvedRequestId}: {ex.Message}");
                DispatchEvent(
                    callbackTarget,
                    new RuntimeAsrScriptEvent(
                        resolvedRequestId,
                        "error",
                        string.Empty,
                        true,
                        ex.Message,
                        onErrorCallback ?? string.Empty,
                        0.0));
            }
            finally
            {
                lock (_sync)
                {
                    _activeRequests.Remove(resolvedRequestId);
                }

                cts.Dispose();
                Console.WriteLine($"[GamePlayer] ASR streaming finished request={resolvedRequestId}.");
            }
        });

        return resolvedRequestId;
    }

    public void StopStreamingRecognition(string? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            StopAllStreamingRecognitions();
            return;
        }

        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _activeRequests.TryGetValue(requestId.Trim(), out CancellationTokenSource? value)
                ? value
                : null;
        }

        cts?.Cancel();
    }

    public void StopAllStreamingRecognitions()
    {
        CancellationTokenSource[] requests;
        lock (_sync)
        {
            requests = _activeRequests.Values.ToArray();
        }

        foreach (CancellationTokenSource request in requests)
        {
            request.Cancel();
        }
    }

    internal void ClearScene()
    {
        StopAllStreamingRecognitions();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearScene();
        (_audioSource as IDisposable)?.Dispose();
        (_recognizer as IDisposable)?.Dispose();
        _recognizer = null;
    }

    private async Task RunStreamingRecognitionAsync(
        RuntimeEntity callbackTarget,
        string requestId,
        string? onPartialCallback,
        string? onCompletedCallback,
        CancellationToken cancellationToken)
    {
        ISpeechRecognizer recognizer = GetRecognizer();
        await using IStreamingSpeechRecognitionSession session = recognizer.CreateStreamingSession(
            new StreamingSpeechRecognitionOptions
            {
                Language = ResolveLanguage(),
                TranslateToEnglish = ResolveTranslateToEnglish(),
                PartialResultInterval = TimeSpan.FromSeconds(Math.Max(0.05f, _settings.PartialResultIntervalSeconds))
            });

        Task updatesTask = Task.Run(async () =>
        {
            await foreach (SpeechRecognitionUpdate update in session.GetUpdatesAsync())
            {
                string eventName = update.IsFinal ? "completed" : "partial";
                DispatchEvent(
                    callbackTarget,
                    new RuntimeAsrScriptEvent(
                        requestId,
                        eventName,
                        update.Text ?? string.Empty,
                        update.IsFinal,
                        string.Empty,
                        update.IsFinal ? onCompletedCallback ?? string.Empty : onPartialCallback ?? string.Empty,
                        update.Offset.TotalSeconds));
            }
        }, cancellationToken);

        AudioCaptureOptions captureOptions = CreateCaptureOptions();
        Console.WriteLine(
            $"[GamePlayer] ASR capture open request={requestId}, device={captureOptions.DeviceIndex?.ToString() ?? "default"}, " +
            $"sampleRate={captureOptions.SampleRate}, channels={captureOptions.Channels}, framesPerBuffer={captureOptions.FramesPerBuffer}");

        var chunkCount = 0;

        try
        {
            await foreach (AudioChunk chunk in _audioSource.CaptureAsync(captureOptions, cancellationToken).ConfigureAwait(false))
            {
                chunkCount++;
                await session.WriteAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed)
        {
        }
        finally
        {
            Console.WriteLine($"[GamePlayer] ASR capture complete request={requestId}, chunks={chunkCount}");
            await session.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await updatesTask.ConfigureAwait(false);
    }

    private ISpeechRecognizer GetRecognizer()
    {
        if (_recognizer is not null)
        {
            return _recognizer;
        }

        if (string.Equals(_settings.Provider, "whisper", StringComparison.OrdinalIgnoreCase))
        {
            _recognizer = new WhisperNetSpeechRecognizer(
                new WhisperNetRecognizerOptions
                {
                    ModelPath = ResolveRequiredPath(_settings.Whisper.ModelPath, "Asr.Whisper.ModelPath"),
                    Language = string.IsNullOrWhiteSpace(_settings.Whisper.Language) ? "auto" : _settings.Whisper.Language,
                    TranslateToEnglish = _settings.Whisper.TranslateToEnglish,
                    UseGpu = _settings.Whisper.UseGpu,
                    Threads = Math.Max(1, _settings.Whisper.Threads),
                    SampleRate = Math.Max(8000, _settings.Whisper.SampleRate)
                },
                NullLogger<WhisperNetSpeechRecognizer>.Instance);
            return _recognizer;
        }

        _recognizer = new SherpaOnnxSpeechRecognizer(
            new SherpaOnnxRecognizerOptions
            {
                ModelKind = ParseSherpaModelKind(_settings.Sherpa.ModelKind),
                TokensPath = ResolveRequiredPath(_settings.Sherpa.TokensPath, "Asr.Sherpa.TokensPath"),
                EncoderPath = ResolveOptionalPath(_settings.Sherpa.EncoderPath),
                DecoderPath = ResolveOptionalPath(_settings.Sherpa.DecoderPath),
                JoinerPath = ResolveOptionalPath(_settings.Sherpa.JoinerPath),
                ModelPath = ResolveOptionalPath(_settings.Sherpa.ModelPath),
                Language = string.IsNullOrWhiteSpace(_settings.Sherpa.Language) ? "zh" : _settings.Sherpa.Language,
                Provider = string.IsNullOrWhiteSpace(_settings.Sherpa.Provider) ? "cpu" : _settings.Sherpa.Provider,
                SampleRate = Math.Max(8000, _settings.Sherpa.SampleRate),
                FeatureDim = Math.Max(1, _settings.Sherpa.FeatureDim),
                Threads = Math.Max(1, _settings.Sherpa.Threads),
                DecodingMethod = string.IsNullOrWhiteSpace(_settings.Sherpa.DecodingMethod) ? "greedy_search" : _settings.Sherpa.DecodingMethod
            },
            NullLogger<SherpaOnnxSpeechRecognizer>.Instance);
        return _recognizer;
    }

    private string ResolveLanguage()
    {
        return string.Equals(_settings.Provider, "whisper", StringComparison.OrdinalIgnoreCase)
            ? _settings.Whisper.Language
            : _settings.Sherpa.Language;
    }

    private bool ResolveTranslateToEnglish()
    {
        return string.Equals(_settings.Provider, "whisper", StringComparison.OrdinalIgnoreCase)
            && _settings.Whisper.TranslateToEnglish;
    }

    private AudioCaptureOptions CreateCaptureOptions()
    {
        return new AudioCaptureOptions
        {
            DeviceIndex = _settings.InputDeviceIndex,
            SampleRate = Math.Max(1, _settings.Capture.SampleRate),
            Channels = Math.Max(1, _settings.Capture.Channels),
            FramesPerBuffer = (uint)Math.Max(0, _settings.Capture.FramesPerBuffer)
        };
    }

    private AudioData CreateWarmupAudio()
    {
        int sampleRate = string.Equals(_settings.Provider, "whisper", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(8000, _settings.Whisper.SampleRate)
            : Math.Max(8000, _settings.Sherpa.SampleRate);
        int channels = Math.Max(1, _settings.Capture.Channels);
        int frameCount = Math.Max(sampleRate / 4, 1024);
        float[] samples = new float[frameCount * channels];
        return new AudioData(samples, new AudioFormat(sampleRate, channels));
    }

    private string ResolveRequiredPath(string? path, string settingName)
    {
        string? resolved = ResolveOptionalPath(path);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException($"{settingName} is required when ASR is enabled.");
        }

        return resolved;
    }

    private string? ResolveOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = GameProjectPath.NormalizePathText(path);
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        if (trimmed.StartsWith("project:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return GameProjectPath.ToAbsolute(_projectDirectory, trimmed);
        }

        string appRelative = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed));
        if (File.Exists(appRelative) || Directory.Exists(appRelative))
        {
            return appRelative;
        }

        string projectRelative = GameProjectPath.ToAbsolute(_projectDirectory, trimmed);
        if (File.Exists(projectRelative) || Directory.Exists(projectRelative))
        {
            return projectRelative;
        }

        return appRelative;
    }

    private static SherpaOnnxRecognizerModelKind ParseSherpaModelKind(string? modelKind)
    {
        return Enum.TryParse(modelKind, ignoreCase: true, out SherpaOnnxRecognizerModelKind parsed)
            ? parsed
            : SherpaOnnxRecognizerModelKind.OnlineTransducer;
    }

    private void EnsureEnabled()
    {
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Project ASR is disabled. Enable Project.Asr.Enabled in GameEditor or game.project.json.");
        }

        if (!_microphoneInputAvailable)
        {
            throw new InvalidOperationException($"Project ASR microphone input is unavailable: {_microphoneUnavailableReason}");
        }
    }

    private static string ResolveRequestId(string? requestId)
    {
        return string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : requestId.Trim();
    }

    private void DispatchEvent(RuntimeEntity target, RuntimeAsrScriptEvent scriptEvent)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _dispatchScriptEvent(target, scriptEvent);
        });
    }
}
