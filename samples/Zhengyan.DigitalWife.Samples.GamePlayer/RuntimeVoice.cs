using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenAL;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeVoice : IDisposable
{
    private readonly GamePlayerGame _game;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly string _projectDirectory;
    private readonly GameProjectVoiceSettings _settings;
    private readonly Dictionary<string, SpeechTransformUpdater> _speechUpdaters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveSpeech> _activeSpeeches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private SpeechDictionarySet? _dictionaries;
    private SherpaOnnxTextToSpeechSynthesizer? _synthesizer;
    private bool _disposed;

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
    }

    public bool IsEnabled => _settings.Enabled;

    public void Preload()
    {
        if (!_settings.Enabled || !_settings.PreloadOnSceneLoad)
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
            SpeechTransformUpdater updater = entity.CreateSpeechUpdater(dictionaries, _settings.LipSync.VowelMorphMap);
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
        if (!_settings.Enabled || !entity.IsPmxModel || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string utteranceText = text.Trim();
        RuntimeVoiceOptions effectiveOptions = options ?? new RuntimeVoiceOptions();

        _ = Task.Run(async () =>
        {
            try
            {
                await SpeakAsync(entity, utteranceText, effectiveOptions).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Entity speech failed for '{entity.Name}': {ex}");
            }
        });
    }

    public void Stop(RuntimeEntity entity)
    {
        _dispatcher.Post(() =>
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
        _synthesizer?.Dispose();
        _synthesizer = null;
    }

    private async Task SpeakAsync(RuntimeEntity entity, string text, RuntimeVoiceOptions options)
    {
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

        _dispatcher.Post(() => PlaySpeechOnMainThread(entity, text, audio, volume, options.OnCompleted));
    }

    private void PlaySpeechOnMainThread(RuntimeEntity entity, string text, AudioData audio, float volume, Action? onCompleted)
    {
        if (_game.Audio is null)
        {
            Console.Error.WriteLine(_game.AudioStatusMessage ?? "Audio is unavailable.");
            InvokeCompletion(onCompleted);
            return;
        }

        AttachEntity(entity);
        AudioClip clip = _game.Audio.CreateClip(
            $"tts:{entity.Id}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            audio.Samples,
            audio.Format.SampleRate,
            audio.Format.Channels);

        AudioSource source = _game.Audio.CreateSource(clip);
        source.Volume = volume;
        source.Play();

        lock (_sync)
        {
            if (_activeSpeeches.Remove(entity.Id, out ActiveSpeech? previous))
            {
                previous.Dispose();
            }

            _activeSpeeches[entity.Id] = new ActiveSpeech(source, clip);
        }

        if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? updater))
        {
            updater.Start(text, CalculateFramePeriod(text, audio.Duration), isLoop: false);
        }

        WaitForSpeechCompletion(entity, source, audio.Duration, onCompleted);
    }

    private void WaitForSpeechCompletion(RuntimeEntity entity, AudioSource source, TimeSpan expectedDuration, Action? onCompleted)
    {
        _ = Task.Run(async () =>
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + expectedDuration + TimeSpan.FromSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50).ConfigureAwait(false);
                TaskCompletionSource<bool> completionProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _dispatcher.Post(() =>
                {
                    try
                    {
                        completionProbe.TrySetResult(source.State != SourceState.Playing);
                    }
                    catch (Exception ex)
                    {
                        completionProbe.TrySetException(ex);
                    }
                });

                bool completed;
                try
                {
                    completed = await completionProbe.Task.ConfigureAwait(false);
                }
                catch
                {
                    completed = true;
                }

                if (completed)
                {
                    break;
                }
            }

            _dispatcher.Post(() =>
            {
                if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? finishedUpdater))
                {
                    finishedUpdater.Stop(resetFace: true);
                }

                bool isCurrentSpeech = false;
                lock (_sync)
                {
                    if (_activeSpeeches.TryGetValue(entity.Id, out ActiveSpeech? active)
                        && ReferenceEquals(active.Source, source))
                    {
                        _activeSpeeches.Remove(entity.Id);
                        active.Dispose();
                        isCurrentSpeech = true;
                    }
                }

                if (isCurrentSpeech)
                {
                    InvokeCompletion(onCompleted);
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

    private TimeSpan CalculateFramePeriod(string text, TimeSpan audioDuration)
    {
        try
        {
            SpeechDictionarySet dictionaries = EnsureSpeechDictionaries();
            string kanaText = dictionaries.Kana.ConvertText(text);
            int vowelCount = 0;
            foreach (char kana in kanaText)
            {
                string vowel = dictionaries.Vowel.GetVowel(kana);
                if (_settings.LipSync.VowelMorphMap.ContainsKey(vowel))
                {
                    vowelCount++;
                }
            }

            double milliseconds = vowelCount > 0
                ? audioDuration.TotalMilliseconds / vowelCount
                : 180.0;

            milliseconds = Math.Clamp(
                milliseconds,
                Math.Max(1.0f, _settings.LipSync.MinFramePeriodMilliseconds),
                Math.Max(_settings.LipSync.MinFramePeriodMilliseconds, _settings.LipSync.MaxFramePeriodMilliseconds));
            return TimeSpan.FromMilliseconds(milliseconds);
        }
        catch
        {
            return TimeSpan.FromMilliseconds(150.0);
        }
    }

    private SherpaOnnxTextToSpeechSynthesizer EnsureSynthesizer()
    {
        if (_synthesizer is not null)
        {
            return _synthesizer;
        }

        if (!string.Equals(_settings.TtsProvider, "sherpa-onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported voice TTS provider: {_settings.TtsProvider}");
        }

        string modelPath = ResolveRequiredPath(_settings.ModelPath, "Voice.ModelPath");
        string tokensPath = ResolveRequiredPath(_settings.TokensPath, "Voice.TokensPath");
        _synthesizer = new SherpaOnnxTextToSpeechSynthesizer(
            new SherpaOnnxTtsOptions
            {
                ModelPath = modelPath,
                TokensPath = tokensPath,
                ModelKind = ParseModelKind(_settings.ModelKind),
                LexiconPath = ResolveOptionalPath(_settings.LexiconPath),
                DataDirectory = ResolveOptionalPath(_settings.DataDirectory),
                DictDirectory = ResolveOptionalPath(_settings.DictDirectory),
                VocoderPath = ResolveOptionalPath(_settings.VocoderPath),
                RuleFars = ResolveOptionalPath(_settings.RuleFars),
                RuleFsts = ResolveRuleFsts(_settings.RuleFsts),
                Provider = string.IsNullOrWhiteSpace(_settings.InferenceProvider) ? "cpu" : _settings.InferenceProvider,
                Threads = Math.Max(1, _settings.Threads)
            },
            NullLogger<SherpaOnnxTextToSpeechSynthesizer>.Instance);

        return _synthesizer;
    }

    private SpeechDictionarySet EnsureSpeechDictionaries()
    {
        if (_dictionaries is not null)
        {
            return _dictionaries;
        }

        string dictionaryDirectory = ResolveOptionalPath(_settings.LipSync.DictionaryDirectory)
            ?? Path.Combine(AppContext.BaseDirectory, "Resources", "SpeechLipSyncDictionaries");
        SpeechDictionaryLanguage language = Enum.TryParse(
            _settings.LipSync.DictionaryLanguage,
            ignoreCase: true,
            out SpeechDictionaryLanguage parsed)
            ? parsed
            : SpeechDictionaryLanguage.Chinese;

        _dictionaries = SpeechDictionarySet.LoadFromDirectory(dictionaryDirectory, language);
        return _dictionaries;
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

        string trimmed = path.Trim();
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

    private sealed class ActiveSpeech(AudioSource source, AudioClip clip) : IDisposable
    {
        public AudioSource Source { get; } = source;

        public AudioClip Clip { get; } = clip;

        public void Dispose()
        {
            Source.Dispose();
            Clip.Dispose();
        }
    }
}
