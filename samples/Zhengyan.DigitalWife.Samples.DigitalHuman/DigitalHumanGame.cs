using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Zhengyan.DigitalWife.Realtime.OpenAI;

namespace Zhengyan.DigitalWife.Samples.DigitalHuman;

internal sealed class DigitalHumanGame : Game
{
    private readonly ResolvedDigitalHumanOptions _options;
    private readonly ILogger<DigitalHumanGame> _logger;
    private readonly IAudioSource _audioSource;
    private readonly OpenAiRealtimeClient _realtimeClient;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IDisposable? _ownedAudioPlayer;
    private readonly OrbitCamera _camera = new();
    private readonly MmdCharacterGroup _characters;
    private readonly Random _random = new();
    private readonly ConcurrentQueue<MainThreadWorkItem> _mainThreadQueue = new();
    private readonly object _bubbleSync = new();
    private readonly object _startupSync = new();
    private readonly DialogueBubbleState _bubbleState = new();
    private StartupStatusSnapshot _startupStatus = new(true, "正在启动", "准备语音模型...", 0.0f);
    private readonly List<MmdCharacter> _wearables = [];
    private readonly List<PmxModelComponent> _sceneModels = [];
    private SpeechDictionarySet? _speechDictionaries;
    private SpeechTransformUpdater? _speechUpdater;
    private MotionSelection? _standMotionSelection;
    private MotionSelection? _waitMotionSelection;
    private MotionSelection? _walkMotionSelection;
    private MotionSelection? _runMotionSelection;
    private MotionBlendState? _motionTransition;

    private CancellationTokenSource? _shutdownCts;
    private Task? _startupTask;
    private Task? _assistantLoopTask;
    private DialogueBubbleOverlayComponent? _bubbleOverlay;
    private AudioClip? _bgmClip;
    private AudioSource? _bgmSource;
    private MmdCharacter? _body;
    private int _mainThreadId;
    private DigitalHumanState _state = DigitalHumanState.WaitingForWakeWord;
    private CharacterMotionGroup _activeMotionGroup = CharacterMotionGroup.Stand;
    private bool _lastNumber1Down;
    private bool _lastNumber2Down;
    private bool _lastNumber3Down;
    private bool _lastNumber4Down;

    public DigitalHumanGame(
        ResolvedDigitalHumanOptions options,
        IServiceProvider services,
        ILogger<DigitalHumanGame> logger)
        : base(new GameOptions
        {
            Title = "Zhengyan.DigitalWife Digital Human",
            WindowSize = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            ClearColor = new Vector4(0.08f, 0.09f, 0.12f, 1.0f),
            UseOpenCL = true,
            EnableAudio = true,
            AnimationTimingMode = AnimationTimingMode.TimeSynchronized
        })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audioSource = services.GetRequiredService<IAudioSource>();
        _realtimeClient = services.GetRequiredService<OpenAiRealtimeClient>();
        if (_options.Audio.PlaybackBackend == AudioPlaybackBackend.OpenAL)
        {
            GameAudioPlayer gameAudioPlayer = new(
                () => Audio,
                RunOnMainThreadAsync,
                () => AudioStatusMessage);
            _audioPlayer = gameAudioPlayer;
            _ownedAudioPlayer = gameAudioPlayer;
        }
        else
        {
            _audioPlayer = services.GetRequiredService<IAudioPlayer>();
        }

        _logger.LogInformation("Digital human speech playback backend: {Backend}", _options.Audio.PlaybackBackend);
        _characters = new MmdCharacterGroup(this, _camera);
    }

    public DigitalHumanState State => _state;

    internal IReadOnlyList<PmxModelComponent> ShadowCasterModels =>
        _characters.Characters.Select(static item => item.ModelComponent).ToArray();

    internal bool HasGroundShadowReceiver =>
        _sceneModels.Count == 0 || _options.Scene.Models.Any(static item => item.ReceivesGroundShadow);

    public string StatusText => _state switch
    {
        DigitalHumanState.WaitingForWakeWord => "等待唤醒",
        DigitalHumanState.WaitingForUserInput => "等待指令",
        DigitalHumanState.Thinking => "思考中",
        DigitalHumanState.Speaking => "正在说话",
        _ => _state.ToString()
    };

    protected override void Initialize()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;

        if (!WindowIconLoader.TrySetWindowIconFromFile(Window, _options.WindowIconPath))
        {
            _logger.LogWarning("Window icon was not set because the configured file was missing or invalid: {IconPath}", _options.WindowIconPath);
        }
    }

    protected override void LoadContent()
    {
        _camera.SetLookAt(_options.Scene.Camera.Position, _options.Scene.Camera.Target);
        _camera.Fov = _options.Scene.Camera.Fov;

        AddComponent(new OrbitCameraController(_camera)
        {
            OrbitSensitivity = 0.2f,
            PanSensitivity = 1.0f,
            ZoomSensitivity = 1.0f,
            KeyboardPanSpeed = 4.0f
        });

        LoadCharacterModels();
        LoadSceneModels();
        _ = AddComponent(new GroundShadowPassComponent(this)
        {
            DrawOrder = 110
        });
        TryStartBackgroundMusic();

        _bubbleOverlay = AddComponent(new DialogueBubbleOverlayComponent(
            this,
            _camera,
            GetBubbleSnapshot,
            TryGetBubbleAnchorWorldPosition,
            GetStartupStatusSnapshot)
        {
            DrawOrder = int.MaxValue
        });

        PlayMotionGroup(CharacterMotionGroup.Stand);
        HideBubble();

        _shutdownCts = new CancellationTokenSource();
        _startupTask = StartBackgroundWork(() => RunStartupAsync(_shutdownCts.Token), _shutdownCts.Token);
    }

    protected override void Update(GameTime gameTime)
    {
        PumpMainThreadQueue();
        UpdateMotionTransition(gameTime.ElapsedGameTime);
        HandleDebugMotionHotkeys();
    }

    protected override void UnloadContent()
    {
        if (_shutdownCts is not null)
        {
            _shutdownCts.Cancel();
            Task?[] tasks = [_startupTask, _assistantLoopTask];
            foreach (Task? task in tasks)
            {
                if (task is null)
                {
                    continue;
                }

                try
                {
                    task.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is OperationCanceledException))
                {
                }
                catch (AggregateException)
                {
                }
            }
        }

        _bgmSource?.Dispose();
        _bgmClip?.Dispose();
        _ownedAudioPlayer?.Dispose();
        _bgmSource = null;
        _bgmClip = null;
    }

    internal DialogueBubbleSnapshot GetBubbleSnapshot()
    {
        lock (_bubbleSync)
        {
            return new DialogueBubbleSnapshot(
                Visible: _options.Character.SpeechBubble.Enabled && _bubbleState.Visible,
                Width: _options.Character.SpeechBubble.Width,
                ScreenOffset: _options.Character.SpeechBubble.ScreenOffset.ToVector2(),
                ShowUserText: _options.Character.SpeechBubble.ShowUserText,
                HintText: _bubbleState.HintText,
                UserText: _bubbleState.UserText,
                AssistantText: _bubbleState.AssistantText);
        }
    }

    internal StartupStatusSnapshot GetStartupStatusSnapshot()
    {
        lock (_startupSync)
        {
            return _startupStatus;
        }
    }

    internal Vector3? TryGetBubbleAnchorWorldPosition()
    {
        if (_body?.ModelComponent is null || !_body.ModelComponent.IsLoaded)
        {
            return null;
        }

        PmxModelComponent model = _body.ModelComponent;
        Vector3 localHead = new(
            (model.BoundsMin.X + model.BoundsMax.X) * 0.5f,
            model.BoundsMax.Y,
            (model.BoundsMin.Z + model.BoundsMax.Z) * 0.5f);

        return Vector3.Transform(localHead, model.World) + _options.Character.SpeechBubble.WorldOffset.ToVector3();
    }

    private async Task RunAssistantLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? firstQuery = await WaitForWakeWordAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await ActivateConversationAsync(cancellationToken);
                DateTimeOffset returnToStandDeadline = DateTimeOffset.UtcNow + _options.Conversation.ReturnToStandTimeout;

                if (!string.IsNullOrWhiteSpace(firstQuery))
                {
                    await ProcessConversationTurnAsync(firstQuery, cancellationToken);
                    returnToStandDeadline = DateTimeOffset.UtcNow + _options.Conversation.ReturnToStandTimeout;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? userText = await WaitForUserInputAsync(returnToStandDeadline, cancellationToken);
                    if (userText is null)
                    {
                        await ReturnToIdleAsync(cancellationToken);
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(userText))
                    {
                        continue;
                    }

                    await ProcessConversationTurnAsync(userText, cancellationToken);
                    returnToStandDeadline = DateTimeOffset.UtcNow + _options.Conversation.ReturnToStandTimeout;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Digital human conversation loop failed.");
            ShowAssistantBubble(string.Empty, $"对话循环异常：{ex.Message}", string.Empty);
        }
    }

    private async Task RunStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<StartupWarmupStep> steps = BuildStartupWarmupSteps().ToArray();
            if (steps.Count == 0)
            {
                await RunOnMainThreadAsync(() => _startupStatus = new StartupStatusSnapshot(false, string.Empty, string.Empty, 1.0f));
                _assistantLoopTask = StartBackgroundWork(() => RunAssistantLoopAsync(cancellationToken), cancellationToken);
                return;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                StartupWarmupStep step = steps[i];
                float progress = (float)i / steps.Count;
                UpdateStartupStatus(true, "正在加载", step.Label, progress);

                await step.Action(cancellationToken);
            }

            UpdateStartupStatus(true, "正在加载", "语音模型已准备就绪，正在进入主界面...", 1.0f);
            await RunOnMainThreadAsync(() => _startupStatus = new StartupStatusSnapshot(false, string.Empty, string.Empty, 1.0f));

            _assistantLoopTask = StartBackgroundWork(() => RunAssistantLoopAsync(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Digital human startup warmup failed.");
            UpdateStartupStatus(true, "启动失败", ex.Message, 1.0f);
        }
    }

    #if false
    private IEnumerable<StartupWarmupStep> BuildStartupWarmupSteps()
    {
        foreach (ISpeechRecognizer recognizer in _speechRecognizers)
        {
            yield return new StartupWarmupStep(
                $"预热语音识别：{recognizer.Name}",
                cancellationToken => WarmUpSpeechRecognizerAsync(recognizer, cancellationToken));
        }

        yield return new StartupWarmupStep(
            $"预热语音合成：{_tts.Name}",
            WarmUpTextToSpeechAsync);
    }

    private async Task WarmUpSpeechRecognizerAsync(ISpeechRecognizer recognizer, CancellationToken cancellationToken)
    {
        AudioData silence = CreateSilenceAudio(_options.Conversation.UserCapture.SampleRate, _options.Conversation.UserCapture.Channels);
        _ = await recognizer.RecognizeAsync(
            silence,
            new SpeechRecognitionOptions
            {
                Language = "zh",
                EnableTimestamps = true
            },
            cancellationToken);
    }

    private async Task WarmUpTextToSpeechAsync(CancellationToken cancellationToken)
    {
        _ = await _tts.SynthesizeAsync(
            "你好",
            new SpeechSynthesisOptions
            {
                ModelKind = _options.Tts.ModelKind,
                Speed = _options.SpeechOutput.Speed,
                SpeakerId = _options.SpeechOutput.SpeakerId
            },
            cancellationToken);
    }

    private async Task<string?> WaitForWakeWordAsync(CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForWakeWord, CharacterMotionGroup.Stand, cancellationToken);
        HideBubble();
        _history.Clear();

        while (!cancellationToken.IsCancellationRequested)
        {
            SpeechRecognitionResult? recognition = await CaptureAndRecognizeWakeWordAsync(cancellationToken);

            if (recognition is null || string.IsNullOrWhiteSpace(recognition.Text))
            {
                continue;
            }

            _logger.LogInformation("Wake-word stage recognized text: {Text}", recognition.Text);

            if (TryExtractWakeWordTail(recognition.Text, out string? tailText))
            {
                _logger.LogInformation("Wake word recognized from ASR text: {Text}", recognition.Text);
                return tailText;
            }
        }

        return null;
    }

    private async Task<SpeechRecognitionResult?> CaptureAndRecognizeWakeWordAsync(CancellationToken cancellationToken)
    {
        AudioData audio = await _audioSource.RecordAsync(
            _options.Conversation.WakeWordChunkDuration,
            _options.Conversation.WakeWordCapture,
            cancellationToken);

        if (audio.Samples.Length == 0)
        {
            return null;
        }

        AudioData padded = AppendTrailingSilence(audio, _options.Conversation.WakeWordTrailingSilencePadding);
        SpeechRecognitionResult result = await RecognizeWithFallbackAsync(
            padded,
            _options.Conversation.UseFallbackRecognizersForWakeWord,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Text) && LooksLikeWakeWordPrefix(result.Text))
        {
            _logger.LogInformation("Wake-word stage detected possible prefix {Text}; capturing extension chunk.", result.Text);

            AudioData extension = await _audioSource.RecordAsync(
                _options.Conversation.WakeWordExtensionDuration,
                _options.Conversation.WakeWordCapture,
                cancellationToken);

            if (extension.Samples.Length > 0)
            {
                AudioData combined = CombineAudio(audio, extension);
                AudioData combinedPadded = AppendTrailingSilence(combined, _options.Conversation.WakeWordTrailingSilencePadding);
                SpeechRecognitionResult extended = await RecognizeWithFallbackAsync(
                    combinedPadded,
                    _options.Conversation.UseFallbackRecognizersForWakeWord,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(extended.Text))
                {
                    _logger.LogInformation("Wake-word extended recognition text: {Text}", extended.Text);
                    return extended;
                }
            }
        }

        return result;
    }

    private async Task ActivateConversationAsync(CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
        await SpeakPromptAsync(_options.Conversation.WakeAcknowledgementText, cancellationToken);
        ShowHintBubble(_options.Conversation.ListeningPromptText);
    }

    private async Task ReturnToIdleAsync(CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForWakeWord, CharacterMotionGroup.Stand, cancellationToken);
        _history.Clear();
        HideBubble();
    }

    private async Task HandleReturnToStandAsync(CancellationToken cancellationToken)
    {
        string promptText = _options.Conversation.ReturnToStandPromptText.Trim();
        if (!string.IsNullOrWhiteSpace(promptText))
        {
            await EnterStateAsync(DigitalHumanState.Speaking, CharacterMotionGroup.Wait, cancellationToken);
            ShowAssistantBubble(string.Empty, promptText, string.Empty);

            AudioData audio = await _tts.SynthesizeAsync(
                promptText,
                new SpeechSynthesisOptions
                {
                    ModelKind = _options.Tts.ModelKind,
                    Speed = _options.SpeechOutput.Speed,
                    SpeakerId = _options.SpeechOutput.SpeakerId
                },
                cancellationToken);

            AudioData adjusted = ApplyVolume(audio, _options.SpeechOutput.Volume);
            await RunOnMainThreadAsync(() => StartLipSync(promptText, adjusted.Duration));
            await _audioPlayer.PlayAsync(adjusted, cancellationToken);
            await RunOnMainThreadAsync(StopLipSync);
        }

        await ReturnToIdleAsync(cancellationToken);
    }

    private async Task<string?> WaitForUserInputAsync(DateTimeOffset idleDeadline, CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
        ShowHintBubble(_options.Conversation.ListeningPromptText);

        TimeSpan remainingToStand = idleDeadline - DateTimeOffset.UtcNow;
        if (remainingToStand <= TimeSpan.Zero)
        {
            _logger.LogInformation("Conversation idle timeout expired; returning to idle.");
            await HandleReturnToStandAsync(cancellationToken);
            return null;
        }

        TimeSpan captureTimeout = _options.Conversation.PostResponseIdleTimeout <= TimeSpan.Zero
            ? remainingToStand
            : _options.Conversation.PostResponseIdleTimeout < remainingToStand
                ? _options.Conversation.PostResponseIdleTimeout
                : remainingToStand;

        SpeechRecognitionResult? recognition = await CaptureAndRecognizeAsync(
            _options.Conversation.UserCapture,
            captureTimeout,
            allowFallbackRecognizers: true,
            saveCapture: true,
            cancellationToken);

        if (recognition is null)
        {
            if (DateTimeOffset.UtcNow >= idleDeadline)
            {
                _logger.LogInformation("Conversation idle timeout expired; returning to idle.");
                await HandleReturnToStandAsync(cancellationToken);
                return null;
            }

            _logger.LogInformation("User input timed out; keep waiting for the idle deadline.");
            return string.Empty;
        }

        string text = recognition.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation("User input was empty; keep waiting.");
            ShowHintBubble("没有听清，请再说一遍。");
            return string.Empty;
        }

        _logger.LogInformation("Conversation stage recognized text: {Text}", text);

        if (TryExtractWakeWordTail(text, out string? tailText))
        {
            return string.IsNullOrWhiteSpace(tailText) ? string.Empty : tailText;
        }

        return text;
    }

    private async Task ProcessConversationTurnAsync(string userText, CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.Thinking, CharacterMotionGroup.Wait, cancellationToken);
        ShowThinkingBubble(userText);

        string normalizedUserText = userText.Trim();
        List<LlmChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            messages.Add(new LlmChatMessage("system", _options.SystemPrompt));
        }

        messages.AddRange(_history);
        messages.Add(new LlmChatMessage("user", normalizedUserText));

        StringBuilder assistantBuilder = new();
        Channel<string> deltaChannel = Channel.CreateUnbounded<string>();

        Task producer = StartBackgroundWork(async () =>
        {
            try
            {
                await foreach (LlmStreamUpdate update in _llmClient.StreamChatAsync(
                    messages,
                    new LlmRequestOptions { Model = _options.LlmModel },
                    cancellationToken))
                {
                    if (string.IsNullOrEmpty(update.Delta))
                    {
                        continue;
                    }

                    assistantBuilder.Append(update.Delta);
                    ShowAssistantBubble(normalizedUserText, assistantBuilder.ToString(), _options.Conversation.ThinkingText);
                    await deltaChannel.Writer.WriteAsync(update.Delta, cancellationToken);
                }

                deltaChannel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                deltaChannel.Writer.TryComplete(ex);
                throw;
            }
        }, cancellationToken);

        Task speaker = StartBackgroundWork(async () =>
        {
            bool enteredSpeakingState = false;

            async IAsyncEnumerable<string> EnumerateDeltas([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                await foreach (string delta in deltaChannel.Reader.ReadAllAsync(token))
                {
                    yield return delta;
                }
            }

            await foreach (string sentence in _sentenceChunker.ChunkAsync(EnumerateDeltas(cancellationToken), cancellationToken: cancellationToken))
            {
                if (!enteredSpeakingState)
                {
                    enteredSpeakingState = true;
                    await EnterStateAsync(DigitalHumanState.Speaking, CharacterMotionGroup.Wait, cancellationToken);
                }

                AudioData audio = await _tts.SynthesizeAsync(
                    sentence,
                    new SpeechSynthesisOptions
                    {
                        ModelKind = _options.Tts.ModelKind,
                        Speed = _options.SpeechOutput.Speed,
                        SpeakerId = _options.SpeechOutput.SpeakerId
                    },
                    cancellationToken);

                AudioData adjusted = ApplyVolume(audio, _options.SpeechOutput.Volume);
                await RunOnMainThreadAsync(() => StartLipSync(sentence, adjusted.Duration));
                await _audioPlayer.PlayAsync(adjusted, cancellationToken);
                await RunOnMainThreadAsync(StopLipSync);
            }
        }, cancellationToken);

        try
        {
            await Task.WhenAll(producer, speaker);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while processing assistant turn.");
            ShowAssistantBubble(normalizedUserText, "抱歉，我刚才出了点问题。", string.Empty);
            return;
        }

        string assistantText = assistantBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(assistantText))
        {
            assistantText = "……";
        }

        AddHistory(normalizedUserText, assistantText);
        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
        ShowAssistantBubble(normalizedUserText, assistantText, _options.Conversation.ListeningPromptText);
    }

    private async Task<SpeechRecognitionResult?> CaptureAndRecognizeAsync(
        VoiceActivityCaptureOptions captureOptions,
        TimeSpan timeout,
        bool allowFallbackRecognizers,
        bool saveCapture,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        AudioData? audio = null;
        try
        {
            audio = await _audioSource.RecordUntilSilenceAsync(captureOptions, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (audio is null || audio.Samples.Length == 0)
        {
            return null;
        }

        string? savedCapturePath = null;
        if (saveCapture && !string.IsNullOrWhiteSpace(_options.CapturedAudioDirectory))
        {
            savedCapturePath = await SaveCapturedAudioAsync(audio, cancellationToken);
        }

        try
        {
            return await RecognizeWithFallbackAsync(audio, allowFallbackRecognizers, cancellationToken);
        }
        finally
        {
            if (_options.DeleteCapturedAudioAfterRecognition
                && !string.IsNullOrWhiteSpace(savedCapturePath)
                && File.Exists(savedCapturePath))
            {
                try
                {
                    File.Delete(savedCapturePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete captured audio file {Path}.", savedCapturePath);
                }
            }
        }
    }

    private async Task<SpeechRecognitionResult> RecognizeWithFallbackAsync(
        AudioData audio,
        bool allowFallbackRecognizers,
        CancellationToken cancellationToken)
    {
        if (_speechRecognizers.Count == 0)
        {
            throw new InvalidOperationException("No speech recognizers are registered.");
        }

        SpeechRecognitionResult? last = null;
        IEnumerable<ISpeechRecognizer> recognizers = allowFallbackRecognizers
            ? _speechRecognizers
            : _speechRecognizers.Take(1);

        foreach (ISpeechRecognizer recognizer in recognizers)
        {
            _logger.LogInformation("Recognizing speech with provider {Provider}.", recognizer.Name);
            last = await recognizer.RecognizeAsync(
                audio,
                new SpeechRecognitionOptions
                {
                    Language = "zh",
                    EnableTimestamps = true
                },
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(last.Text))
            {
                return last;
            }
        }

        return last ?? new SpeechRecognitionResult { Text = string.Empty };
    }

    private async Task<string?> SaveCapturedAudioAsync(AudioData audio, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CapturedAudioDirectory))
        {
            return null;
        }

        Directory.CreateDirectory(_options.CapturedAudioDirectory);
        string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav";
        string path = Path.Combine(_options.CapturedAudioDirectory, fileName);
        await WaveFile.WriteAsync(path, audio, cancellationToken: cancellationToken);
        return path;
    }

    private async Task SpeakPromptAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await EnterStateAsync(DigitalHumanState.Speaking, CharacterMotionGroup.Wait, cancellationToken);
        ShowAssistantBubble(string.Empty, text, string.Empty);

        AudioData audio = await _tts.SynthesizeAsync(
            text,
            new SpeechSynthesisOptions
            {
                ModelKind = _options.Tts.ModelKind,
                Speed = _options.SpeechOutput.Speed,
                SpeakerId = _options.SpeechOutput.SpeakerId
            },
            cancellationToken);

        AudioData adjusted = ApplyVolume(audio, _options.SpeechOutput.Volume);
        await RunOnMainThreadAsync(() => StartLipSync(text, adjusted.Duration));
        await _audioPlayer.PlayAsync(adjusted, cancellationToken);
        await RunOnMainThreadAsync(StopLipSync);
        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
    }

    private bool TryExtractWakeWordTail(string text, out string? tailText)
    {
        foreach (string wakeWord in _options.Conversation.WakeWords)
        {
            if (string.IsNullOrWhiteSpace(wakeWord))
            {
                continue;
            }

            int index = text.IndexOf(wakeWord, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                StringBuilder builder = new(text);
                builder.Remove(index, wakeWord.Length);
                tailText = builder.ToString().Trim().Trim('，', ',', '。', '.', '！', '!', '？', '?', '：', ':');
                return true;
            }

            string normalizedText = NormalizeWakeWordText(text);
            string normalizedWakeWord = NormalizeWakeWordText(wakeWord);
            if (!string.IsNullOrWhiteSpace(normalizedWakeWord)
                && normalizedText.Contains(normalizedWakeWord, StringComparison.OrdinalIgnoreCase))
            {
                tailText = string.Empty;
                return true;
            }
        }

        tailText = null;
        return false;
    }

    private void AddHistory(string userText, string assistantText)
    {
        _history.Add(new LlmChatMessage("user", userText));
        _history.Add(new LlmChatMessage("assistant", assistantText));

        int maxMessages = Math.Max(0, _options.Conversation.HistoryMaxMessages) * 2;
        if (maxMessages == 0)
        {
            _history.Clear();
            return;
        }

        while (_history.Count > maxMessages)
        {
            _history.RemoveAt(0);
        }
    }

    #endif

    private IEnumerable<StartupWarmupStep> BuildStartupWarmupSteps()
    {
        yield return new StartupWarmupStep(
            "连接实时语音服务",
            WarmUpRealtimeConnectionAsync);
    }

    private async Task WarmUpRealtimeConnectionAsync(CancellationToken cancellationToken)
    {
        await _realtimeClient.ConnectAsync(cancellationToken);
        await _realtimeClient.UpdateSessionAsync(_options.RealtimeSession, cancellationToken);
        await _realtimeClient.ResetConversationAsync(cancellationToken);
    }

    private async Task<string?> WaitForWakeWordAsync(CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForWakeWord, CharacterMotionGroup.Stand, cancellationToken);
        HideBubble();
        await _realtimeClient.ResetConversationAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            OpenAiRealtimeTranscriptionResult? recognition = await CaptureAndTranscribeWakeWordAsync(cancellationToken);
            if (recognition is null || string.IsNullOrWhiteSpace(recognition.Text))
            {
                continue;
            }

            _logger.LogInformation("Wake-word stage recognized text: {Text}", recognition.Text);

            if (!TryExtractWakeWordTail(recognition.Text, out string? tailText))
            {
                continue;
            }

            _logger.LogInformation("Wake word recognized from ASR text: {Text}", recognition.Text);
            if (!string.IsNullOrWhiteSpace(tailText))
            {
                await EnsureRemoteUserConversationItemAsync(tailText, cancellationToken);
            }

            return tailText;
        }

        return null;
    }

    private async Task<OpenAiRealtimeTranscriptionResult?> CaptureAndTranscribeWakeWordAsync(CancellationToken cancellationToken)
    {
        AudioData audio = await _audioSource.RecordAsync(
            _options.Conversation.WakeWordChunkDuration,
            _options.Conversation.WakeWordCapture,
            cancellationToken);

        if (audio.Samples.Length == 0)
        {
            return null;
        }

        AudioData padded = AppendTrailingSilence(audio, _options.Conversation.WakeWordTrailingSilencePadding);
        OpenAiRealtimeTranscriptionResult? result = await TranscribeAudioAsync(
            padded,
            deleteConversationItemAfterTranscription: true,
            saveCapture: false,
            cancellationToken);

        if (result is null
            || string.IsNullOrWhiteSpace(result.Text)
            || !LooksLikeWakeWordPrefix(result.Text))
        {
            return result;
        }

        _logger.LogInformation("Wake-word stage detected possible prefix {Text}; capturing extension chunk.", result.Text);

        AudioData extension = await _audioSource.RecordAsync(
            _options.Conversation.WakeWordExtensionDuration,
            _options.Conversation.WakeWordCapture,
            cancellationToken);

        if (extension.Samples.Length == 0)
        {
            return result;
        }

        AudioData combined = CombineAudio(audio, extension);
        AudioData combinedPadded = AppendTrailingSilence(combined, _options.Conversation.WakeWordTrailingSilencePadding);
        OpenAiRealtimeTranscriptionResult? extended = await TranscribeAudioAsync(
            combinedPadded,
            deleteConversationItemAfterTranscription: true,
            saveCapture: false,
            cancellationToken);

        if (extended is not null && !string.IsNullOrWhiteSpace(extended.Text))
        {
            _logger.LogInformation("Wake-word extended recognition text: {Text}", extended.Text);
            return extended;
        }

        return result;
    }

    private async Task ActivateConversationAsync(CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
        await ShowTransientPromptAsync(_options.Conversation.WakeAcknowledgementText, cancellationToken);
        ShowHintBubble(_options.Conversation.ListeningPromptText);
    }

    private async Task ReturnToIdleAsync(CancellationToken cancellationToken)
    {
        await _realtimeClient.ResetConversationAsync(cancellationToken);
        await EnterStateAsync(DigitalHumanState.WaitingForWakeWord, CharacterMotionGroup.Stand, cancellationToken);
        HideBubble();
    }

    private async Task HandleReturnToStandAsync(CancellationToken cancellationToken)
    {
        string promptText = _options.Conversation.ReturnToStandPromptText.Trim();
        if (!string.IsNullOrWhiteSpace(promptText))
        {
            await ShowTransientPromptAsync(promptText, cancellationToken);
        }

        await ReturnToIdleAsync(cancellationToken);
    }

    private async Task<string?> WaitForUserInputAsync(DateTimeOffset idleDeadline, CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
        ShowHintBubble(_options.Conversation.ListeningPromptText);

        TimeSpan remainingToStand = idleDeadline - DateTimeOffset.UtcNow;
        if (remainingToStand <= TimeSpan.Zero)
        {
            _logger.LogInformation("Conversation idle timeout expired; returning to idle.");
            await HandleReturnToStandAsync(cancellationToken);
            return null;
        }

        TimeSpan captureTimeout = _options.Conversation.PostResponseIdleTimeout <= TimeSpan.Zero
            ? remainingToStand
            : _options.Conversation.PostResponseIdleTimeout < remainingToStand
                ? _options.Conversation.PostResponseIdleTimeout
                : remainingToStand;

        OpenAiRealtimeTranscriptionResult? recognition = await CaptureAndTranscribeAsync(
            _options.Conversation.UserCapture,
            captureTimeout,
            deleteConversationItemAfterTranscription: false,
            saveCapture: true,
            cancellationToken);

        if (recognition is null)
        {
            if (DateTimeOffset.UtcNow >= idleDeadline)
            {
                _logger.LogInformation("Conversation idle timeout expired; returning to idle.");
                await HandleReturnToStandAsync(cancellationToken);
                return null;
            }

            _logger.LogInformation("User input timed out; keep waiting for the idle deadline.");
            return string.Empty;
        }

        string text = recognition.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation("User input was empty; keep waiting.");
            await DeleteConversationItemIfPresentAsync(recognition.ItemId, cancellationToken);
            ShowHintBubble("没有听清，请再说一遍。");
            return string.Empty;
        }

        _logger.LogInformation("Conversation stage recognized text: {Text}", text);

        if (!TryExtractWakeWordTail(text, out string? tailText))
        {
            return text;
        }

        await DeleteConversationItemIfPresentAsync(recognition.ItemId, cancellationToken);
        if (string.IsNullOrWhiteSpace(tailText))
        {
            return string.Empty;
        }

        await EnsureRemoteUserConversationItemAsync(tailText, cancellationToken);
        return tailText;
    }

    private async Task ProcessConversationTurnAsync(string userText, CancellationToken cancellationToken)
    {
        await EnterStateAsync(DigitalHumanState.Thinking, CharacterMotionGroup.Wait, cancellationToken);
        ShowThinkingBubble(userText);

        string normalizedUserText = userText.Trim();

        try
        {
            string assistantText = await PlayRealtimeResponseAsync(normalizedUserText, cancellationToken);
            if (string.IsNullOrWhiteSpace(assistantText))
            {
                assistantText = "……";
            }

            await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
            ShowAssistantBubble(normalizedUserText, assistantText, _options.Conversation.ListeningPromptText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while processing assistant turn.");
            ShowAssistantBubble(normalizedUserText, "抱歉，我刚才出了点问题。", string.Empty);
        }
    }

    private async Task<string> PlayRealtimeResponseAsync(string userText, CancellationToken cancellationToken)
    {
        StringBuilder assistantBuilder = new();
        Channel<AudioChunk> audioChannel = Channel.CreateUnbounded<AudioChunk>();
        Task? playbackTask = null;
        bool enteredSpeakingState = false;
        bool lipSyncLoopStarted = false;
        Task? lipSyncStartTask = null;
        int lastLipSyncLength = 0;
        string finalText = string.Empty;

        try
        {
            await foreach (OpenAiRealtimeResponseUpdate update in _realtimeClient.CreateResponseAsync(cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(update.AssistantTranscript))
                {
                    assistantBuilder.Clear();
                    assistantBuilder.Append(update.AssistantTranscript);

                    if (!enteredSpeakingState)
                    {
                        enteredSpeakingState = true;
                        await EnterStateAsync(DigitalHumanState.Speaking, CharacterMotionGroup.Wait, cancellationToken);
                    }

                    ShowAssistantBubble(userText, assistantBuilder.ToString(), string.Empty);

                    if (lipSyncLoopStarted && assistantBuilder.Length - lastLipSyncLength >= 8)
                    {
                        lastLipSyncLength = assistantBuilder.Length;
                        string lipSyncText = assistantBuilder.ToString();
                        await RunOnMainThreadAsync(() => StartLipSyncLoop(lipSyncText));
                    }
                }

                if (update.AudioChunk is not null)
                {
                    if (!enteredSpeakingState)
                    {
                        enteredSpeakingState = true;
                        await EnterStateAsync(DigitalHumanState.Speaking, CharacterMotionGroup.Wait, cancellationToken);
                    }

                    if (playbackTask is null)
                    {
                        playbackTask = _audioPlayer.PlayAsync(
                            ReadRealtimeAudioAsync(audioChannel.Reader, cancellationToken),
                            update.AudioChunk.Format,
                            cancellationToken);
                    }

                    if (!lipSyncLoopStarted)
                    {
                        lipSyncLoopStarted = true;
                        lastLipSyncLength = assistantBuilder.Length;
                        string lipSyncText = assistantBuilder.Length > 0 ? assistantBuilder.ToString() : userText;
                        TimeSpan lipSyncDelay = GetLipSyncStartDelay(update.AudioChunk.Format);
                        lipSyncStartTask = StartLipSyncAfterDelayAsync(lipSyncText, lipSyncDelay, cancellationToken);
                    }

                    await audioChannel.Writer.WriteAsync(ApplyVolume(update.AudioChunk, _options.SpeechOutput.Volume), cancellationToken);
                }

                if (update.IsCompleted)
                {
                    finalText = string.IsNullOrWhiteSpace(update.FinalAssistantText)
                        ? assistantBuilder.ToString().Trim()
                        : update.FinalAssistantText.Trim();
                    break;
                }
            }
        }
        finally
        {
            audioChannel.Writer.TryComplete();
            if (playbackTask is not null)
            {
                await playbackTask;
            }

            if (lipSyncStartTask is not null)
            {
                try
                {
                    await lipSyncStartTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (lipSyncLoopStarted)
            {
                await RunOnMainThreadAsync(StopLipSync);
            }
        }

        return finalText;
    }

    private async Task<OpenAiRealtimeTranscriptionResult?> CaptureAndTranscribeAsync(
        VoiceActivityCaptureOptions captureOptions,
        TimeSpan timeout,
        bool deleteConversationItemAfterTranscription,
        bool saveCapture,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        AudioData? audio = null;
        try
        {
            audio = await _audioSource.RecordUntilSilenceAsync(captureOptions, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (audio is null || audio.Samples.Length == 0)
        {
            return null;
        }

        return await TranscribeAudioAsync(audio, deleteConversationItemAfterTranscription, saveCapture, cancellationToken);
    }

    private async Task<OpenAiRealtimeTranscriptionResult?> TranscribeAudioAsync(
        AudioData audio,
        bool deleteConversationItemAfterTranscription,
        bool saveCapture,
        CancellationToken cancellationToken)
    {
        if (audio.Samples.Length == 0)
        {
            return null;
        }

        string? savedCapturePath = null;
        if (saveCapture && !string.IsNullOrWhiteSpace(_options.CapturedAudioDirectory))
        {
            savedCapturePath = await SaveCapturedAudioAsync(audio, cancellationToken);
        }

        try
        {
            return await _realtimeClient.TranscribeAsync(audio, deleteConversationItemAfterTranscription, cancellationToken);
        }
        finally
        {
            if (_options.DeleteCapturedAudioAfterRecognition
                && !string.IsNullOrWhiteSpace(savedCapturePath)
                && File.Exists(savedCapturePath))
            {
                try
                {
                    File.Delete(savedCapturePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete captured audio file {Path}.", savedCapturePath);
                }
            }
        }
    }

    private async Task EnsureRemoteUserConversationItemAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _realtimeClient.CreateConversationItemAsync(new OpenAiRealtimeConversationItem
        {
            Type = "message",
            Status = "completed",
            Role = "user",
            Content =
            [
                new OpenAiRealtimeContentPart
                {
                    Type = "input_text",
                    Text = text.Trim()
                }
            ]
        }, cancellationToken: cancellationToken);
    }

    private async Task DeleteConversationItemIfPresentAsync(string? itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        try
        {
            await _realtimeClient.DeleteConversationItemAsync(itemId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Realtime conversation item {ItemId}.", itemId);
        }
    }

    private async Task ShowTransientPromptAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await EnterStateAsync(DigitalHumanState.Speaking, CharacterMotionGroup.Wait, cancellationToken);
        ShowAssistantBubble(string.Empty, text, string.Empty);
        await RunOnMainThreadAsync(() => StartLipSyncLoop(text));

        try
        {
            await PlayRealtimePromptAsync(text, cancellationToken);
        }
        finally
        {
            await RunOnMainThreadAsync(StopLipSync);
        }

        await EnterStateAsync(DigitalHumanState.WaitingForUserInput, CharacterMotionGroup.Wait, cancellationToken);
    }

    private async Task PlayRealtimePromptAsync(string text, CancellationToken cancellationToken)
    {
        AudioData directAudio = await _realtimeClient.SynthesizeTextAsync(
            text,
            new OpenAiAudioSpeechRequest
            {
                Model = _options.RealtimeSession.Model,
                Voice = _options.RealtimeSession.Audio.Output.Voice,
                ResponseFormat = "wav"
            },
            cancellationToken);

        AudioData adjustedDirectAudio = ApplyVolume(directAudio, _options.SpeechOutput.Volume);
        TimeSpan lipSyncDelay = GetLipSyncStartDelay(adjustedDirectAudio.Format);
        Task playbackTask = _audioPlayer.PlayAsync(adjustedDirectAudio, cancellationToken);
        if (lipSyncDelay > TimeSpan.Zero)
        {
            await Task.Delay(lipSyncDelay, cancellationToken);
        }

        TimeSpan lipSyncDuration = adjustedDirectAudio.Duration > lipSyncDelay
            ? adjustedDirectAudio.Duration - lipSyncDelay
            : adjustedDirectAudio.Duration;
        await RunOnMainThreadAsync(() => StartLipSync(text, lipSyncDuration));
        await playbackTask;
        return;

        #if false
        OpenAiRealtimeResponseRequest request = new()
        {
            Conversation = "none",
            Instructions = $"请只输出以下这句话，不要添加任何其它内容，也不要解释：{text}",
            OutputModalities = ["audio"],
            Audio = new OpenAiRealtimeResponseAudioOptions
            {
                Format = _options.RealtimeSession.Audio.Output.Format,
                Voice = _options.RealtimeSession.Audio.Output.Voice
            },
            MaxOutputTokens = Math.Max(32, text.Length * 4),
            Temperature = 0.0f
        };
        request.Instructions = $"Return exactly the following sentence and do not add anything else: {text}";

        Channel<AudioChunk> audioChannel = Channel.CreateUnbounded<AudioChunk>();
        Task? playbackTask = null;

        try
        {
            await foreach (OpenAiRealtimeResponseUpdate update in _realtimeClient.CreateResponseAsync(request, cancellationToken))
            {
                if (update.AudioChunk is not null)
                {
                    if (playbackTask is null)
                    {
                        playbackTask = _audioPlayer.PlayAsync(
                            ReadRealtimeAudioAsync(audioChannel.Reader, cancellationToken),
                            update.AudioChunk.Format,
                            cancellationToken);
                    }

                    await audioChannel.Writer.WriteAsync(ApplyVolume(update.AudioChunk, _options.SpeechOutput.Volume), cancellationToken);
                }

                if (update.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            audioChannel.Writer.TryComplete();
            if (playbackTask is not null)
            {
                await playbackTask;
            }
        }
        #endif
    }

    private async IAsyncEnumerable<AudioChunk> ReadRealtimeAudioAsync(
        ChannelReader<AudioChunk> reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (AudioChunk chunk in reader.ReadAllAsync(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task<string?> SaveCapturedAudioAsync(AudioData audio, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CapturedAudioDirectory))
        {
            return null;
        }

        Directory.CreateDirectory(_options.CapturedAudioDirectory);
        string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav";
        string path = Path.Combine(_options.CapturedAudioDirectory, fileName);
        await WaveFile.WriteAsync(path, audio, cancellationToken: cancellationToken);
        return path;
    }

    private bool TryExtractWakeWordTail(string text, out string? tailText)
    {
        foreach (string wakeWord in _options.Conversation.WakeWords)
        {
            if (string.IsNullOrWhiteSpace(wakeWord))
            {
                continue;
            }

            int index = text.IndexOf(wakeWord, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                StringBuilder builder = new(text);
                builder.Remove(index, wakeWord.Length);
                tailText = builder.ToString().Trim().Trim(',', '.', '!', '?', ':', ';', '\uFF0C', '\u3002', '\uFF01', '\uFF1F', '\uFF1A', '\uFF1B');
                return true;
            }

            string normalizedText = NormalizeWakeWordText(text);
            string normalizedWakeWord = NormalizeWakeWordText(wakeWord);
            if (!string.IsNullOrWhiteSpace(normalizedWakeWord)
                && normalizedText.Contains(normalizedWakeWord, StringComparison.OrdinalIgnoreCase))
            {
                tailText = string.Empty;
                return true;
            }
        }

        tailText = null;
        return false;
    }

    private async Task EnterStateAsync(DigitalHumanState state, CharacterMotionGroup motionGroup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RunOnMainThreadAsync(() =>
        {
            _state = state;
            PlayMotionGroup(motionGroup);
        });
    }

    private void LoadCharacterModels()
    {
        _body = _characters.AddCharacter(
            _options.Character.Body.Path,
            name: _options.Character.Body.Name,
            configureModel: model => ConfigureModel(model, _options.Character.Body));

        TryAttachSpeechUpdater();

        foreach (ResolvedWearableOptions wearable in _options.Character.Wearables)
        {
            MmdCharacter character = _characters.AddCharacter(
                wearable.Path,
                name: wearable.Name,
                configureModel: model => ConfigureModel(model, wearable));

            character.IsPlaying = false;
            RelationTransformUpdater relation = _characters.BindRelation(character, _body, wearable.BindComponentTransform);
            relation.BindLighting = wearable.BindLighting;
            _wearables.Add(character);
        }
    }

    private void LoadSceneModels()
    {
        foreach (ResolvedSceneModelOptions sceneModel in _options.Scene.Models)
        {
            PmxModelComponent model = AddComponent(new PmxModelComponent(sceneModel.Path)
            {
                Camera = _camera,
                Position = sceneModel.Position,
                Scale = sceneModel.Scale,
                Rotation = sceneModel.Rotation,
                IsPlaying = sceneModel.IsPlaying,
                EnablePhysical = sceneModel.EnablePhysical,
                EnableEdge = sceneModel.EnableEdge,
                EnableShadow = sceneModel.EnableShadow,
                DrawShadowInMainPass = sceneModel.DrawShadowInMainPass,
                ShouldUpdatePoseEvaluator = ShouldUpdatePmxPose,
                OffscreenPoseUpdateIntervalSeconds = 0.12f
            });

            ApplyLighting(model);
            _sceneModels.Add(model);
        }
    }

    private void ConfigureModel(PmxModelComponent model, ResolvedModelOptions options)
    {
        model.Camera = _camera;
        model.Scale = options.Scale;
        model.Position = options.Position;
        model.Rotation = options.Rotation;
        model.EnablePhysical = options.EnablePhysical;
        model.EnableEdge = options.EnableEdge;
        model.EnableShadow = options.EnableShadow;
        model.DrawShadowInMainPass = false;
        model.ShouldUpdatePoseEvaluator = ShouldUpdatePmxPose;
        model.OffscreenPoseUpdateIntervalSeconds = 0.12f;
        ApplyLighting(model);
    }

    private bool ShouldUpdatePmxPose(PmxModelComponent model)
    {
        float radius = MathF.Max(Vector3.Distance(model.BoundsMin, model.BoundsMax) * 0.5f, 0.5f);
        Vector3 center = Vector3.Transform((model.BoundsMin + model.BoundsMax) * 0.5f, model.World);
        return VisibilityCulling.IsBoundingSphereVisible(_camera, center, radius);
    }

    private void TryStartBackgroundMusic()
    {
        if (Audio is null)
        {
            _logger.LogInformation("Background music skipped because OpenAL audio is unavailable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Scene.BackgroundMusic.Path))
        {
            return;
        }

        string bgmPath = _options.Scene.BackgroundMusic.Path;
        if (!File.Exists(bgmPath))
        {
            _logger.LogWarning("Background music file not found: {Path}", bgmPath);
            return;
        }

        _bgmClip = Audio.LoadClip(bgmPath);
        _bgmSource = Audio.CreateSource(_bgmClip);
        _bgmSource.Looping = _options.Scene.BackgroundMusic.Loop;
        _bgmSource.Volume = _options.Scene.BackgroundMusic.Volume;
        _bgmSource.Play();
    }

    private void PlayMotionGroup(CharacterMotionGroup group)
    {
        if (_body is null)
        {
            return;
        }

        if (TryPlayTransitionMotionGroup(group))
        {
            return;
        }

        _motionTransition = null;

        IReadOnlyList<ResolvedMotionClipOptions> motions = GetMotionGroupOptions(group);

        if (motions.Count == 0)
        {
            return;
        }

        ResolvedMotionClipOptions motion = motions[_random.Next(motions.Count)];
        ApplySingleMotion(motion);
    }

    private bool TryPlayTransitionMotionGroup(CharacterMotionGroup targetGroup)
    {
        if (_body is null)
        {
            return true;
        }

        if (_motionTransition is null && _activeMotionGroup == targetGroup)
        {
            if (!TryGetCachedMotionSelection(targetGroup, out MotionSelection selection))
            {
                selection = SelectMotionSelection(targetGroup);
                ApplyMotionImmediately(targetGroup, selection);
                return true;
            }

            if (_body.ModelComponent.MotionPath is null
                || !string.Equals(_body.ModelComponent.MotionPath, selection.Path, StringComparison.OrdinalIgnoreCase))
            {
                ApplyMotionImmediately(targetGroup, selection);
            }

            return true;
        }

        if (_motionTransition is not null)
        {
            MotionBlendState transition = _motionTransition.Value;
            if (transition.TargetGroup == targetGroup)
            {
                return true;
            }

            FinalizeMotionTransition(transition);
        }

        MotionSelection source = GetOrCreateMotionSelection(_activeMotionGroup);
        MotionSelection target = SelectMotionSelection(targetGroup);
        float sourceTimeSeconds = _body.ModelComponent.AnimationTimeSeconds;
        TimeSpan transitionDuration = GetMotionTransitionDuration(_activeMotionGroup, targetGroup);

        if (transitionDuration <= TimeSpan.Zero || string.Equals(source.Path, target.Path, StringComparison.OrdinalIgnoreCase))
        {
            ApplyMotionImmediately(targetGroup, target);
            return true;
        }

        _body.ModelComponent.SetMotionLayerWeight(source.Path, 1.0f);
        if (!_body.ModelComponent.TrySetMotionLayerWeight(target.Path, 0.0f))
        {
            _body.ModelComponent.AddMotionLayer(target.Path, 0.0f, target.ResetPhysicsOnLoop);
        }
        _body.ModelComponent.AnimationTimeSeconds = sourceTimeSeconds;
        _body.ModelComponent.LoopMotion = true;
        _body.ModelComponent.IsPlaying = true;
        _motionTransition = new MotionBlendState(_activeMotionGroup, targetGroup, source, target, transitionDuration, TimeSpan.Zero);
        return true;
    }

    private void ApplyMotionImmediately(CharacterMotionGroup targetGroup, MotionSelection target)
    {
        if (_body is null)
        {
            return;
        }

        _body.ModelComponent.SetMotionLayers([
            new MotionLayerDefinition(target.Path, 1.0f, target.ResetPhysicsOnLoop)
        ]);
        _body.ModelComponent.LoopMotion = true;
        _body.ModelComponent.IsPlaying = true;
        _body.ModelComponent.ResetPhysicsOnMotionLoop = target.ResetPhysicsOnLoop;

        CacheMotionSelection(targetGroup, target);
        _activeMotionGroup = targetGroup;
        _motionTransition = null;
    }

    private void ApplySingleMotion(ResolvedMotionClipOptions motion)
    {
        if (_body is null)
        {
            return;
        }

        _body.ModelComponent.SetMotionLayers([
            new MotionLayerDefinition(motion.Path, 1.0f, motion.ResetPhysicsOnLoop)
        ]);
        _body.ModelComponent.LoopMotion = true;
        _body.ModelComponent.IsPlaying = true;
        _body.ModelComponent.ResetPhysicsOnMotionLoop = motion.ResetPhysicsOnLoop;
        _motionTransition = null;
    }

    private void UpdateMotionTransition(TimeSpan elapsed)
    {
        if (_body is null || _motionTransition is null)
        {
            return;
        }

        MotionBlendState transition = _motionTransition.Value;
        TimeSpan updatedElapsed = transition.Elapsed + elapsed;
        float progress = transition.Duration <= TimeSpan.Zero
            ? 1.0f
            : (float)Math.Clamp(updatedElapsed.TotalSeconds / transition.Duration.TotalSeconds, 0.0, 1.0);

        _body.ModelComponent.SetMotionLayerWeight(transition.Source.Path, 1.0f - progress);
        _body.ModelComponent.SetMotionLayerWeight(transition.Target.Path, progress);

        if (progress >= 1.0f)
        {
            FinalizeMotionTransition(transition);
            return;
        }

        _motionTransition = transition with { Elapsed = updatedElapsed };
    }

    private void FinalizeMotionTransition(MotionBlendState transition)
    {
        if (_body is null)
        {
            return;
        }

        _body.ModelComponent.SetMotionLayerWeight(transition.Target.Path, 1.0f);
        _body.ModelComponent.RemoveMotionLayer(transition.Source.Path, skipPhysicsOnNextPlayFrame: false);
        _body.ModelComponent.LoopMotion = true;
        _body.ModelComponent.IsPlaying = true;
        _body.ModelComponent.ResetPhysicsOnMotionLoop = transition.Target.ResetPhysicsOnLoop;

        if (transition.Target.ResetPhysicsOnLoop)
        {
            _body.ModelComponent.ResetPhysics();
        }

        CacheMotionSelection(transition.TargetGroup, transition.Target);
        _activeMotionGroup = transition.TargetGroup;
        _motionTransition = null;
    }

    private MotionSelection GetOrCreateMotionSelection(CharacterMotionGroup group)
    {
        MotionSelection? current = group switch
        {
            CharacterMotionGroup.Stand => _standMotionSelection,
            CharacterMotionGroup.Wait => _waitMotionSelection,
            CharacterMotionGroup.Walk => _walkMotionSelection,
            CharacterMotionGroup.Run => _runMotionSelection,
            _ => null
        };

        if (current is MotionSelection selection)
        {
            return selection;
        }

        selection = SelectMotionSelection(group);
        CacheMotionSelection(group, selection);
        return selection;
    }

    private MotionSelection SelectMotionSelection(CharacterMotionGroup group)
    {
        IReadOnlyList<ResolvedMotionClipOptions> motions = GetMotionGroupOptions(group);
        if (motions.Count == 0)
        {
            throw new InvalidOperationException($"No motion clips configured for {group}.");
        }

        ResolvedMotionClipOptions motion = motions[_random.Next(motions.Count)];
        return new MotionSelection(motion.Path, motion.ResetPhysicsOnLoop);
    }

    private bool TryGetCachedMotionSelection(CharacterMotionGroup group, out MotionSelection selection)
    {
        MotionSelection? current = group switch
        {
            CharacterMotionGroup.Stand => _standMotionSelection,
            CharacterMotionGroup.Wait => _waitMotionSelection,
            CharacterMotionGroup.Walk => _walkMotionSelection,
            CharacterMotionGroup.Run => _runMotionSelection,
            _ => null
        };

        if (current is MotionSelection value)
        {
            selection = value;
            return true;
        }

        selection = default;
        return false;
    }

    private void CacheMotionSelection(CharacterMotionGroup group, MotionSelection selection)
    {
        switch (group)
        {
            case CharacterMotionGroup.Stand:
                _standMotionSelection = selection;
                break;
            case CharacterMotionGroup.Wait:
                _waitMotionSelection = selection;
                break;
            case CharacterMotionGroup.Walk:
                _walkMotionSelection = selection;
                break;
            case CharacterMotionGroup.Run:
                _runMotionSelection = selection;
                break;
        }
    }

    private IReadOnlyList<ResolvedMotionClipOptions> GetMotionGroupOptions(CharacterMotionGroup group) =>
        group switch
        {
            CharacterMotionGroup.Stand => _options.Character.Actions.Stand,
            CharacterMotionGroup.Wait => _options.Character.Actions.Wait,
            CharacterMotionGroup.Walk => _options.Character.Actions.Walk,
            CharacterMotionGroup.Run => _options.Character.Actions.Run,
            _ => []
        };

    private TimeSpan GetMotionTransitionDuration(CharacterMotionGroup sourceGroup, CharacterMotionGroup targetGroup)
    {
        ResolvedMotionTransitionOptions? transition = _options.Conversation.MotionTransitions.FirstOrDefault(item =>
            item.Source == sourceGroup && item.Target == targetGroup);

        if (transition is not null)
        {
            return transition.Duration;
        }

        return _options.Conversation.MotionTransitionDuration;
    }

    private void ApplyLighting(PmxModelComponent model)
    {
        model.LightColor = _options.Scene.Lighting.DirectionalLightColor;
        model.AmbientLightColor = _options.Scene.Lighting.AmbientLightColor;
        model.AmbientLightStrength = _options.Scene.Lighting.AmbientLightStrength;
        model.LightDirection = _options.Scene.Lighting.DirectionalLightDirection;
        model.ShadowColor = _options.Scene.Lighting.ShadowColor;
        model.GroundShadowPlaneHeight = _options.Scene.Lighting.GroundShadowPlaneHeight;
    }

    private void TryAttachSpeechUpdater()
    {
        if (_body is null)
        {
            return;
        }

        try
        {
            _speechDictionaries = SpeechDictionarySet.LoadFromDirectory(
                _options.Conversation.SpeechDictionaryDirectory,
                _options.Conversation.SpeechDictionaryLanguage);
            _speechUpdater = _characters.AttachSpeech(_body, _speechDictionaries);
            _speechUpdater.Stop(resetFace: true);
        }
        catch (Exception ex)
        {
            _speechDictionaries = null;
            _speechUpdater = null;
            _logger.LogWarning(ex, "Failed to initialize speech lip-sync dictionaries.");
        }
    }

    private void StartLipSync(string text, TimeSpan audioDuration)
    {
        if (_speechUpdater is null || _speechDictionaries is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        int vowelCount = CountVowels(text, _speechDictionaries);
        double framePeriodMilliseconds = vowelCount > 0
            ? audioDuration.TotalMilliseconds / vowelCount
            : 180.0;

        framePeriodMilliseconds = Math.Clamp(framePeriodMilliseconds, 70.0, 320.0);
        _speechUpdater.Start(text, TimeSpan.FromMilliseconds(framePeriodMilliseconds), isLoop: false);
    }

    private void StartLipSyncLoop(string text)
    {
        if (_speechUpdater is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _speechUpdater.Start(text, TimeSpan.FromMilliseconds(150.0), isLoop: true);
    }

    private void StopLipSync()
    {
        _speechUpdater?.Stop(resetFace: true);
    }

    private void HandleDebugMotionHotkeys()
    {
        if (!_options.Character.Actions.EnableDebugMotionHotkeys)
        {
            return;
        }

        bool number1 = Input.IsKeyDown(Silk.NET.Input.Key.Number1);
        bool number2 = Input.IsKeyDown(Silk.NET.Input.Key.Number2);
        bool number3 = Input.IsKeyDown(Silk.NET.Input.Key.Number3);
        bool number4 = Input.IsKeyDown(Silk.NET.Input.Key.Number4);

        if (number1 && !_lastNumber1Down)
        {
            PlayMotionGroup(CharacterMotionGroup.Stand);
        }

        if (number2 && !_lastNumber2Down)
        {
            PlayMotionGroup(CharacterMotionGroup.Wait);
        }

        if (number3 && !_lastNumber3Down)
        {
            PlayMotionGroup(CharacterMotionGroup.Walk);
        }

        if (number4 && !_lastNumber4Down)
        {
            PlayMotionGroup(CharacterMotionGroup.Run);
        }

        _lastNumber1Down = number1;
        _lastNumber2Down = number2;
        _lastNumber3Down = number3;
        _lastNumber4Down = number4;
    }

    private void PumpMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out MainThreadWorkItem workItem))
        {
            try
            {
                workItem.Action();
                workItem.Completion?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                workItem.Completion?.TrySetException(ex);
            }
        }
    }

    private Task RunOnMainThreadAsync(Action action)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainThreadQueue.Enqueue(new MainThreadWorkItem(action, completion));
        return completion.Task;
    }

    private static Task StartBackgroundWork(Func<Task> work, CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
            async () =>
            {
                TryLowerCurrentThreadPriority();
                await work();
            },
            cancellationToken,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private static void TryLowerCurrentThreadPriority()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        }
        catch
        {
        }
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

    private static AudioChunk ApplyVolume(AudioChunk chunk, float volume)
    {
        if (Math.Abs(volume - 1.0f) < 0.0001f)
        {
            return chunk;
        }

        float[] scaled = new float[chunk.Samples.Length];
        ReadOnlySpan<float> source = chunk.Samples.Span;
        for (int i = 0; i < source.Length; i++)
        {
            scaled[i] = Math.Clamp(source[i] * volume, -1.0f, 1.0f);
        }

        return new AudioChunk(scaled, chunk.Format, chunk.Offset, chunk.IsFinal);
    }

    private async Task StartLipSyncAfterDelayAsync(string text, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await RunOnMainThreadAsync(() => StartLipSyncLoop(text));
    }

    private static AudioData AppendTrailingSilence(AudioData audio, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return audio;
        }

        int silenceSamples = Math.Max(1, (int)Math.Round(duration.TotalSeconds * audio.Format.SampleRate * audio.Format.Channels));
        float[] combined = new float[audio.Samples.Length + silenceSamples];
        Array.Copy(audio.Samples, combined, audio.Samples.Length);
        return new AudioData(combined, audio.Format);
    }

    private static AudioData CombineAudio(AudioData first, AudioData second)
    {
        AudioData normalizedSecond = second.Format == first.Format
            ? second
            : second.ToMono().Resample(first.Format.SampleRate);

        float[] combined = new float[first.Samples.Length + normalizedSecond.Samples.Length];
        Array.Copy(first.Samples, combined, first.Samples.Length);
        Array.Copy(normalizedSecond.Samples, 0, combined, first.Samples.Length, normalizedSecond.Samples.Length);
        return new AudioData(combined, first.Format);
    }

    private static AudioData CreateSilenceAudio(int sampleRate, int channels)
    {
        int sampleCount = Math.Max(1, sampleRate / 5) * Math.Max(1, channels);
        return new AudioData(new float[sampleCount], new AudioFormat(sampleRate, Math.Max(1, channels)));
    }

    private TimeSpan GetLipSyncStartDelay(AudioFormat format)
    {
        TimeSpan estimated = _audioPlayer is IAudioPlaybackTiming timing
            ? timing.GetEstimatedOutputLatency(format)
            : TimeSpan.FromMilliseconds(80);

        if (estimated < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        TimeSpan minimum = TimeSpan.FromMilliseconds(45);
        return estimated < minimum ? minimum : estimated;
    }

    private static int CountVowels(string text, SpeechDictionarySet dictionaries)
    {
        string kanaText = dictionaries.Kana.ConvertText(text);
        int count = 0;

        foreach (char kana in kanaText)
        {
            string vowel = dictionaries.Vowel.GetVowel(kana);
            if (vowel is "あ" or "い" or "う" or "え" or "お")
            {
                count++;
            }
        }

        return count;
    }

    private void HideBubble()
    {
        lock (_bubbleSync)
        {
            _bubbleState.Visible = false;
            _bubbleState.HintText = string.Empty;
            _bubbleState.UserText = string.Empty;
            _bubbleState.AssistantText = string.Empty;
        }
    }

    private void UpdateStartupStatus(bool visible, string title, string message, float progress)
    {
        lock (_startupSync)
        {
            _startupStatus = new StartupStatusSnapshot(
                visible,
                title,
                message,
                Math.Clamp(progress, 0.0f, 1.0f));
        }
    }

    private void ShowHintBubble(string hint)
    {
        lock (_bubbleSync)
        {
            _bubbleState.Visible = true;
            _bubbleState.HintText = hint;
            _bubbleState.UserText = string.Empty;
            _bubbleState.AssistantText = string.Empty;
        }
    }

    private void ShowThinkingBubble(string userText)
    {
        lock (_bubbleSync)
        {
            _bubbleState.Visible = true;
            _bubbleState.HintText = _options.Conversation.ThinkingText;
            _bubbleState.UserText = userText;
            _bubbleState.AssistantText = string.Empty;
        }
    }

    private void ShowAssistantBubble(string userText, string assistantText, string hintText)
    {
        lock (_bubbleSync)
        {
            _bubbleState.Visible = true;
            _bubbleState.HintText = hintText;
            _bubbleState.UserText = userText;
            _bubbleState.AssistantText = assistantText;
        }
    }

    private static string NormalizeWakeWordText(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char ch in value)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                continue;
            }

            switch (ch)
            {
                case '，':
                case '。':
                case '！':
                case '？':
                case '：':
                case '；':
                case '、':
                case '（':
                case '）':
                case '“':
                case '”':
                case '‘':
                case '’':
                    continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private bool LooksLikeWakeWordPrefix(string recognizedText)
    {
        string normalizedRecognized = NormalizeWakeWordText(recognizedText);
        if (string.IsNullOrWhiteSpace(normalizedRecognized))
        {
            return false;
        }

        foreach (string wakeWord in _options.Conversation.WakeWords)
        {
            string normalizedWakeWord = NormalizeWakeWordText(wakeWord);
            if (normalizedWakeWord.Length <= normalizedRecognized.Length)
            {
                continue;
            }

            if (normalizedWakeWord.StartsWith(normalizedRecognized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class DialogueBubbleState
    {
        public bool Visible { get; set; }

        public string HintText { get; set; } = string.Empty;

        public string UserText { get; set; } = string.Empty;

        public string AssistantText { get; set; } = string.Empty;
    }

    private readonly record struct MainThreadWorkItem(Action Action, TaskCompletionSource<bool>? Completion);

    private readonly record struct StartupWarmupStep(string Label, Func<CancellationToken, Task> Action);
}

internal readonly record struct StartupStatusSnapshot(
    bool Visible,
    string Title,
    string Message,
    float Progress);

internal readonly record struct MotionSelection(string Path, bool ResetPhysicsOnLoop);

internal readonly record struct MotionBlendState(
    CharacterMotionGroup SourceGroup,
    CharacterMotionGroup TargetGroup,
    MotionSelection Source,
    MotionSelection Target,
    TimeSpan Duration,
    TimeSpan Elapsed);

internal enum DigitalHumanState
{
    WaitingForWakeWord = 0,
    WaitingForUserInput = 1,
    Thinking = 2,
    Speaking = 3
}

internal enum CharacterMotionGroup
{
    Stand = 0,
    Wait = 1,
    Walk = 2,
    Run = 3
}
