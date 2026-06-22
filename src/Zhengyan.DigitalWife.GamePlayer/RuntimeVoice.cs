using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeVoice : IDisposable
{
    private readonly GamePlayerGame _game;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly string _projectDirectory;
    private readonly GameProjectVoiceSettings _settings;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IDisposable _ownedAudioPlayer;
    private readonly Dictionary<string, SpeechTransformUpdater> _speechUpdaters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveSpeech> _activeSpeeches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly object _resourceSync = new();
    private SpeechDictionarySet? _dictionaries;
    private SherpaOnnxTextToSpeechSynthesizer? _synthesizer;
    private bool _disposed;
    private int _sceneVersion;

    internal RuntimeVoice(
        GamePlayerGame game,
        MainThreadDispatcher dispatcher,
        string projectDirectory,
        GameProjectVoiceSettings settings)
    {
        _game = game;
        _dispatcher = dispatcher;
        _projectDirectory = projectDirectory;
        _settings = settings;
        _ownedAudioPlayer = settings.PlaybackBackend == AudioPlaybackBackend.PortAudio
            ? new PortAudioSpeakerPlayer(
                NullLogger<PortAudioSpeakerPlayer>.Instance,
                new PortAudioRuntimeOptions
                {
                    OutputDeviceIndex = settings.OutputDeviceIndex
                })
            : new GameAudioPlayer(
                () => game.Audio,
                dispatcher.InvokeAsync,
                () => game.AudioStatusMessage);
        _audioPlayer = (IAudioPlayer)_ownedAudioPlayer;
        Console.WriteLine($"[GamePlayer] Voice playback backend: {_settings.PlaybackBackend}");
        if (_settings.PlaybackBackend == AudioPlaybackBackend.PortAudio)
        {
            string deviceText = settings.OutputDeviceIndex?.ToString() ?? "default";
            Console.WriteLine($"[GamePlayer] Voice PortAudio output device: {deviceText}");
        }
    }

    public bool IsEnabled => _settings.Enabled;

    public void Preload()
    {
        if (_disposed || !_settings.Enabled || !_settings.PreloadOnSceneLoad)
        {
            return;
        }

        _ = EnsureSynthesizer();
        if (_settings.LipSync.Enabled)
        {
            _ = EnsureSpeechDictionaries();
        }

        string warmUpText = string.IsNullOrWhiteSpace(_settings.WarmUpText)
            ? "你好"
            : _settings.WarmUpText;
        try
        {
            _ = EnsureSynthesizer().SynthesizeAsync(
                warmUpText,
                new SpeechSynthesisOptions
                {
                    ModelKind = ParseModelKind(_settings.ModelKind),
                    SpeakerId = _settings.DefaultSpeakerId,
                    Speed = Math.Clamp(_settings.DefaultSpeed, 0.1f, 5.0f)
                }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TTS preload failed: {ex.Message}");
        }
    }

    public void AttachEntity(RuntimeEntity entity)
    {
        if (!_settings.Enabled || !_settings.LipSync.Enabled || !entity.IsPmxModel)
        {
            return;
        }

        if (_speechUpdaters.ContainsKey(entity.Id))
        {
            return;
        }

        try
        {
            SpeechDictionarySet dictionaries = EnsureSpeechDictionaries();
            SpeechTransformUpdater updater = entity.CreateSpeechUpdater(
                dictionaries,
                _settings.LipSync.VowelMorphMap,
                ResolveNoMatchFallbackVowel(_settings.LipSync));
            updater.Stop(resetFace: true);
            _speechUpdaters[entity.Id] = updater;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to attach lip-sync to entity '{entity.Name}': {ex.Message}");
        }
    }

    public void Speak(RuntimeEntity entity, string text, int? speakerId = null, float? speed = null, float? volume = null)
    {
        Speak(entity, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            Volume = volume
        });
    }

    public void Speak(RuntimeEntity entity, string text, RuntimeVoiceOptions? options = null)
    {
        RuntimeVoiceOptions effectiveOptions = options ?? new RuntimeVoiceOptions();
        if (_disposed)
        {
            InvokeCompletion(effectiveOptions.OnCompleted);
            return;
        }

        if (!_settings.Enabled)
        {
            Console.Error.WriteLine($"Entity speech ignored for '{entity.Name}' because Voice.Enabled is false.");
            InvokeCompletion(effectiveOptions.OnCompleted);
            return;
        }

        if (!entity.IsPmxModel)
        {
            Console.Error.WriteLine($"Entity speech ignored for '{entity.Name}' because it is not a PMX model.");
            InvokeCompletion(effectiveOptions.OnCompleted);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            InvokeCompletion(effectiveOptions.OnCompleted);
            return;
        }

        string utteranceText = text.Trim();
        int sceneVersion = Volatile.Read(ref _sceneVersion);

        _ = Task.Run(async () =>
        {
            try
            {
                await SpeakAsync(entity, utteranceText, effectiveOptions, sceneVersion).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The scene or player was disposed while speech was being prepared.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Entity speech failed for '{entity.Name}': {ex}");
                _dispatcher.Post(() => InvokeCompletion(effectiveOptions.OnCompleted));
            }
        });
    }

    public void Stop(RuntimeEntity entity)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? updater))
            {
                updater.Stop(resetFace: true);
            }

            lock (_sync)
            {
                if (_activeSpeeches.Remove(entity.Id, out ActiveSpeech? speech))
                {
                    speech.Dispose();
                }
            }
        });
    }

    internal void ClearScene()
    {
        Interlocked.Increment(ref _sceneVersion);

        foreach (SpeechTransformUpdater updater in _speechUpdaters.Values)
        {
            updater.Stop(resetFace: true);
        }

        _speechUpdaters.Clear();

        lock (_sync)
        {
            foreach (ActiveSpeech speech in _activeSpeeches.Values)
            {
                speech.Dispose();
            }

            _activeSpeeches.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ClearScene();
        SherpaOnnxTextToSpeechSynthesizer? synthesizer;
        lock (_resourceSync)
        {
            synthesizer = _synthesizer;
            _synthesizer = null;
            _dictionaries = null;
        }

        synthesizer?.Dispose();
        _ownedAudioPlayer.Dispose();
    }

    private async Task SpeakAsync(RuntimeEntity entity, string text, RuntimeVoiceOptions options, int sceneVersion)
    {
        if (_disposed || sceneVersion != Volatile.Read(ref _sceneVersion))
        {
            return;
        }

        SherpaOnnxTextToSpeechSynthesizer synthesizer = EnsureSynthesizer();
        int speakerId = options.SpeakerId ?? _settings.DefaultSpeakerId;
        float speed = Math.Clamp(options.Speed ?? _settings.DefaultSpeed, 0.1f, 5.0f);
        float volume = Math.Clamp(options.Volume ?? _settings.DefaultVolume, 0.0f, 4.0f);

        AudioData audio = await synthesizer.SynthesizeAsync(
            text,
            new SpeechSynthesisOptions
            {
                ModelKind = ParseModelKind(_settings.ModelKind),
                SpeakerId = speakerId,
                Speed = speed
            }).ConfigureAwait(false);
        AudioData adjustedAudio = ApplyVolume(audio, volume);

        if (sceneVersion != Volatile.Read(ref _sceneVersion))
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (sceneVersion == Volatile.Read(ref _sceneVersion))
            {
                PlaySpeechOnMainThread(entity, text, adjustedAudio, options.OnCompleted);
            }
        });
    }

    private void PlaySpeechOnMainThread(RuntimeEntity entity, string text, AudioData audio, Action? onCompleted)
    {
        if (_settings.PlaybackBackend == AudioPlaybackBackend.OpenAL && _game.Audio is null)
        {
            Console.Error.WriteLine(_game.AudioStatusMessage ?? "Audio is unavailable.");
            InvokeCompletion(onCompleted);
            return;
        }

        AttachEntity(entity);
        CancellationTokenSource cancellation = new();
        Task playbackTask = _audioPlayer.PlayAsync(audio, cancellation.Token);
        ActiveSpeech activeSpeech = new(cancellation, playbackTask);

        lock (_sync)
        {
            if (_activeSpeeches.Remove(entity.Id, out ActiveSpeech? previous))
            {
                previous.Dispose();
            }

            _activeSpeeches[entity.Id] = activeSpeech;
        }

        if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? updater))
        {
            updater.Start(text, CalculateFramePeriod(updater, text, audio.Duration), isLoop: false);
        }

        WaitForSpeechCompletion(entity, activeSpeech, onCompleted);
    }

    private void WaitForSpeechCompletion(RuntimeEntity entity, ActiveSpeech speech, Action? onCompleted)
    {
        _ = Task.Run(async () =>
        {
            bool completedNaturally = false;
            try
            {
                await speech.PlaybackTask.ConfigureAwait(false);
                completedNaturally = !speech.IsCanceled;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Entity speech playback failed for '{entity.Name}': {ex.Message}");
            }

            _dispatcher.Post(() =>
            {
                bool isCurrentSpeech = false;
                lock (_sync)
                {
                    if (_activeSpeeches.TryGetValue(entity.Id, out ActiveSpeech? active)
                        && ReferenceEquals(active, speech))
                    {
                        _activeSpeeches.Remove(entity.Id);
                        active.Dispose();
                        isCurrentSpeech = true;
                    }
                }

                if (isCurrentSpeech)
                {
                    if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? finishedUpdater))
                    {
                        finishedUpdater.Stop(resetFace: true);
                    }

                    if (completedNaturally)
                    {
                        InvokeCompletion(onCompleted);
                    }
                }
            });
        });
    }

    private static void InvokeCompletion(Action? onCompleted)
    {
        if (onCompleted is null)
        {
            return;
        }

        try
        {
            onCompleted();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Speech completion callback failed: {ex.Message}");
        }
    }

    private TimeSpan CalculateFramePeriod(SpeechTransformUpdater updater, string text, TimeSpan audioDuration)
    {
        try
        {
            int vowelCount = updater.CountRecognizedVowels(text);
            if (vowelCount <= 0)
            {
                if (!string.IsNullOrWhiteSpace(updater.NoMatchFallbackVowel))
                {
                    return TimeSpan.FromMilliseconds(Math.Max(
                        Math.Max(1.0, audioDuration.TotalMilliseconds),
                        Math.Max(1.0f, _settings.LipSync.MinFramePeriodMilliseconds)));
                }

                return TimeSpan.FromMilliseconds(180.0);
            }

            double targetDurationMilliseconds = Math.Max(1.0, audioDuration.TotalMilliseconds);
            double milliseconds = Math.Max(
                targetDurationMilliseconds / vowelCount,
                Math.Max(1.0f, _settings.LipSync.MinFramePeriodMilliseconds));
            return TimeSpan.FromMilliseconds(milliseconds);
        }
        catch
        {
            return TimeSpan.FromMilliseconds(150.0);
        }
    }

    private static string? ResolveNoMatchFallbackVowel(GameProjectLipSyncSettings lipSync)
    {
        return lipSync.UseFallbackVowelOnNoMatch
            ? lipSync.GetEffectiveNoMatchFallbackVowel()
            : null;
    }

    private SherpaOnnxTextToSpeechSynthesizer EnsureSynthesizer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_resourceSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_synthesizer is not null)
            {
                return _synthesizer;
            }

            if (!string.Equals(_settings.TtsProvider, "sherpa-onnx", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported voice TTS provider: {_settings.TtsProvider}");
            }

            SpeechSynthesisModelKind modelKind = ParseModelKind(_settings.ModelKind);
            string modelPath = ResolveRequiredPath(_settings.ModelPath, "Voice.ModelPath");
            string tokensPath = ResolveRequiredPath(_settings.TokensPath, "Voice.TokensPath");
            string? dataDirectory = ResolveOptionalPath(_settings.DataDirectory);
            string? dictDirectory = ResolveOptionalPath(_settings.DictDirectory);
            if (modelKind == SpeechSynthesisModelKind.Matcha)
            {
                dataDirectory = ResolveMatchaDataDirectory(modelPath, dataDirectory, dictDirectory);
            }

            _synthesizer = new SherpaOnnxTextToSpeechSynthesizer(
                new SherpaOnnxTtsOptions
                {
                    ModelPath = modelPath,
                    TokensPath = tokensPath,
                    ModelKind = modelKind,
                    LexiconPath = ResolveOptionalPath(_settings.LexiconPath),
                    DataDirectory = dataDirectory,
                    DictDirectory = dictDirectory,
                    VocoderPath = ResolveOptionalPath(_settings.VocoderPath),
                    RuleFars = ResolveOptionalPath(_settings.RuleFars),
                    RuleFsts = ResolveRuleFsts(_settings.RuleFsts),
                    Provider = string.IsNullOrWhiteSpace(_settings.InferenceProvider) ? "cpu" : _settings.InferenceProvider,
                    Threads = Math.Max(1, _settings.Threads)
                },
                NullLogger<SherpaOnnxTextToSpeechSynthesizer>.Instance);

            return _synthesizer;
        }
    }

    private static string ResolveMatchaDataDirectory(string modelPath, string? dataDirectory, string? dictDirectory)
    {
        foreach (string? candidate in new[]
        {
            dataDirectory,
            dictDirectory,
            Path.GetDirectoryName(modelPath)
        })
        {
            string? resolved = ResolveEspeakDataDirectory(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        throw new InvalidOperationException(
            "SherpaOnnx Matcha TTS requires Voice.DataDirectory to point to an espeak-ng-data directory that contains phontab.");
    }

    private static string? ResolveEspeakDataDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        if (File.Exists(Path.Combine(directory, "phontab")))
        {
            return directory;
        }

        string nested = Path.Combine(directory, "espeak-ng-data");
        return File.Exists(Path.Combine(nested, "phontab")) ? nested : null;
    }

    private SpeechDictionarySet EnsureSpeechDictionaries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_resourceSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_dictionaries is not null)
            {
                return _dictionaries;
            }

            string dictionaryDirectory = ResolveOptionalPath(_settings.LipSync.DictionaryDirectory)
                ?? Path.Combine(AppContext.BaseDirectory, "Resources", "SpeechLipSyncDictionaries");
            _dictionaries = SpeechDictionarySet.LoadFromDirectory(
                dictionaryDirectory,
                ResolveDictionaryLanguages(_settings.LipSync));
            return _dictionaries;
        }
    }

    private static IReadOnlyList<SpeechDictionaryLanguage> ResolveDictionaryLanguages(GameProjectLipSyncSettings lipSync)
    {
        List<SpeechDictionaryLanguage> languages = [];
        foreach (string value in lipSync.GetEffectiveDictionaryLanguages())
        {
            if (!Enum.TryParse(value, ignoreCase: true, out SpeechDictionaryLanguage parsed) || languages.Contains(parsed))
            {
                continue;
            }

            languages.Add(parsed);
        }

        if (languages.Count == 0)
        {
            languages.Add(SpeechDictionaryLanguage.Chinese);
        }

        return languages;
    }

    private string ResolveRequiredPath(string path, string settingName)
    {
        string? resolved = ResolveOptionalPath(path);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException($"{settingName} is required when Voice.Enabled is true.");
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

        if (trimmed.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            return GameProjectPath.ToAbsolute(_projectDirectory, trimmed["project:".Length..]);
        }

        if (trimmed.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed["app:".Length..]));
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

    private string? ResolveRuleFsts(string? ruleFsts)
    {
        if (string.IsNullOrWhiteSpace(ruleFsts))
        {
            return null;
        }

        string[] resolved = ruleFsts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => ResolveOptionalPath(item) ?? item)
            .ToArray();
        return string.Join(",", resolved);
    }

    private static SpeechSynthesisModelKind ParseModelKind(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out SpeechSynthesisModelKind kind)
            ? kind
            : SpeechSynthesisModelKind.Vits;
    }

    private static AudioData ApplyVolume(AudioData audio, float volume)
    {
        if (Math.Abs(volume - 1.0f) < 0.0001f)
        {
            return audio;
        }

        float[] scaled = new float[audio.Samples.Length];
        for (int i = 0; i < audio.Samples.Length; i++)
        {
            scaled[i] = Math.Clamp(audio.Samples[i] * volume, -1.0f, 1.0f);
        }

        return new AudioData(scaled, audio.Format);
    }

    private sealed class ActiveSpeech(CancellationTokenSource cancellation, Task playbackTask) : IDisposable
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task PlaybackTask { get; } = playbackTask;

        public bool IsCanceled { get; private set; }

        public void Dispose()
        {
            if (!IsCanceled)
            {
                IsCanceled = true;
                Cancellation.Cancel();
            }

            Cancellation.Dispose();
        }
    }
}
