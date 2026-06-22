using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.RealtimeVoice.Client;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeRealtimeVoice : IDisposable
{
    private const float WakeWordSpeechRmsThreshold = 0.008f;

    private readonly GamePlayerGame _game;
    private readonly string _projectDirectory;
    private readonly GameProjectRealtimeVoiceSettings _settings;
    private readonly GameProjectVoiceSettings _voiceSettings;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly Action<RuntimeEntity, RuntimeRealtimeVoiceScriptEvent> _dispatchScriptEvent;
    private readonly IAudioSource _audioSource;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IDisposable _ownedAudioPlayer;
    private readonly bool _microphoneInputAvailable;
    private readonly string _microphoneUnavailableReason;
    private readonly object _sync = new();
    private readonly object _resourceSync = new();
    private readonly Dictionary<string, CancellationTokenSource> _activeRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpeechTransformUpdater> _speechUpdaters = new(StringComparer.OrdinalIgnoreCase);
    private RealtimeVoiceClient? _client;
    private CancellationTokenSource? _wakeWordMonitorCts;
    private PendingRealtimeUserItem? _pendingTranscribedUserItem;
    private SpeechDictionarySet? _dictionaries;
    private bool _disposed;

    internal RuntimeRealtimeVoice(
        GamePlayerGame game,
        string projectDirectory,
        GameProjectRealtimeVoiceSettings settings,
        GameProjectVoiceSettings voiceSettings,
        MainThreadDispatcher dispatcher,
        Action<RuntimeEntity, RuntimeRealtimeVoiceScriptEvent> dispatchScriptEvent,
        bool microphoneInputAvailable = true,
        string? microphoneUnavailableReason = null)
    {
        _game = game;
        _projectDirectory = projectDirectory;
        _settings = settings;
        _voiceSettings = voiceSettings;
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
        _ownedAudioPlayer = voiceSettings.PlaybackBackend == AudioPlaybackBackend.PortAudio
            ? new PortAudioSpeakerPlayer(
                NullLogger<PortAudioSpeakerPlayer>.Instance,
                new PortAudioRuntimeOptions
                {
                    OutputDeviceIndex = voiceSettings.OutputDeviceIndex
                })
            : new GameAudioPlayer(
                () => _game.Audio,
                _dispatcher.InvokeAsync,
                () => _game.AudioStatusMessage);
        _audioPlayer = (IAudioPlayer)_ownedAudioPlayer;
    }

    public bool Enabled => _settings.Enabled;

    public string BaseUrl => _settings.BaseUrl;

    public string Model => _settings.Model;

    public string Voice => _settings.Voice;

    public bool WakeWordEnabled => _settings.WakeWord.Enabled && _microphoneInputAvailable;

    public IReadOnlyList<string> WakeWords => _settings.WakeWord.Keywords;

    public int? InputDeviceIndex => _settings.InputDeviceIndex;

    public bool MicrophoneInputAvailable => _microphoneInputAvailable;

    public string MicrophoneUnavailableReason => _microphoneInputAvailable ? string.Empty : _microphoneUnavailableReason;

    public bool IsWakeWordMonitoring
    {
        get
        {
            lock (_sync)
            {
                return _wakeWordMonitorCts is not null && !_wakeWordMonitorCts.IsCancellationRequested;
            }
        }
    }

    public void StartWakeWordMonitoring(
        RuntimeEntity callbackTarget,
        string? onDetectedCallback = "wake_word_detected",
        string? onErrorCallback = "wake_word_error")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);
        EnsureEnabled();
        EnsureMicrophoneInputAvailable();

        StopWakeWordMonitoring();

        CancellationTokenSource cts = new();
        lock (_sync)
        {
            _wakeWordMonitorCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                AudioCaptureOptions captureOptions = CreateAudioCaptureOptions(_settings.WakeWord.Capture);
                Console.WriteLine(
                    $"[GamePlayer] RealtimeVoice wake word capture open target={callbackTarget.Name}, " +
                    $"device={captureOptions.DeviceIndex?.ToString() ?? "default"}, " +
                    $"sampleRate={captureOptions.SampleRate}, channels={captureOptions.Channels}, " +
                    $"framesPerBuffer={captureOptions.FramesPerBuffer}");

                await using var captureSession = new ContinuousAudioCaptureSession(
                    _audioSource,
                    captureOptions,
                    cts.Token);

                while (!cts.IsCancellationRequested)
                {
                    string? requestId = Guid.NewGuid().ToString("N");
                    OpenAiRealtimeTranscriptionResult? result = await CaptureAndRecognizeWakeWordAsync(
                        captureSession,
                        cts.Token).ConfigureAwait(false);
                    if (result is null || string.IsNullOrWhiteSpace(result.Text))
                    {
                        continue;
                    }

                    if (!TryExtractWakeWordTail(result.Text, out string? wakeWord, out string? tailText))
                    {
                        continue;
                    }

                    DispatchEvent(
                        callbackTarget,
                        new RuntimeRealtimeVoiceScriptEvent(
                            requestId,
                            "wake_word_detected",
                            tailText ?? string.Empty,
                            string.Empty,
                            string.Empty,
                            true,
                            string.Empty,
                            onDetectedCallback ?? string.Empty,
                            wakeWord ?? string.Empty,
                            result.Text));
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested || _disposed)
            {
            }
            catch (Exception ex)
            {
                DispatchEvent(
                    callbackTarget,
                    new RuntimeRealtimeVoiceScriptEvent(
                        string.Empty,
                        "error",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        true,
                        ex.Message,
                        onErrorCallback ?? string.Empty,
                        string.Empty,
                        string.Empty));
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_wakeWordMonitorCts, cts))
                    {
                        _wakeWordMonitorCts = null;
                    }
                }

                cts.Dispose();
            }
        });
    }

    public void StopWakeWordMonitoring()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _wakeWordMonitorCts;
            _wakeWordMonitorCts = null;
        }

        cts?.Cancel();
    }

    public string StartTranscription(
        RuntimeEntity callbackTarget,
        string? requestId = null,
        float? timeoutSeconds = null,
        string? onCompletedCallback = "realtime_voice_transcription_completed",
        string? onTimeoutCallback = "realtime_voice_timeout",
        string? onErrorCallback = "realtime_voice_error")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);
        EnsureEnabled();
        EnsureMicrophoneInputAvailable();
        StopWakeWordMonitoring();

        string resolvedRequestId = ResolveRequestId(requestId);
        TrackRequest(async cts =>
        {
            RealtimeVoiceClient client = GetClient(cts.Token);
            await DeletePendingTranscribedUserItemBestEffortAsync(client, cts.Token).ConfigureAwait(false);

            RealtimeVoiceCaptureResult capture = await CaptureAndTranscribeUntilTextAsync(
                timeoutSeconds,
                false,
                cts.Token).ConfigureAwait(false);
            if (capture.TimedOut)
            {
                DispatchEvent(
                    callbackTarget,
                    new RuntimeRealtimeVoiceScriptEvent(
                        resolvedRequestId,
                        "timeout",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        true,
                        string.Empty,
                        onTimeoutCallback ?? string.Empty,
                        string.Empty,
                        string.Empty));
                return;
            }

            OpenAiRealtimeTranscriptionResult? result = capture.Result;
            RememberPendingTranscribedUserItem(result);
            DispatchEvent(
                callbackTarget,
                new RuntimeRealtimeVoiceScriptEvent(
                    resolvedRequestId,
                    "transcription_completed",
                    result?.Text ?? string.Empty,
                    string.Empty,
                    result?.Text ?? string.Empty,
                    true,
                    string.Empty,
                    onCompletedCallback ?? string.Empty,
                    string.Empty,
                    result?.Text ?? string.Empty));
        }, callbackTarget, resolvedRequestId, onErrorCallback);
        return resolvedRequestId;
    }

    public string StartResponse(
        RuntimeEntity callbackTarget,
        string userText,
        string? requestId = null,
        string? onDeltaCallback = "realtime_voice_delta",
        string? onCompletedCallback = "realtime_voice_completed",
        string? onErrorCallback = "realtime_voice_error")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);
        EnsureEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);
        StopWakeWordMonitoring();

        string resolvedRequestId = ResolveRequestId(requestId);
        TrackRequest(
            cts => RunResponseAsync(callbackTarget, userText.Trim(), resolvedRequestId, onDeltaCallback, onCompletedCallback, cts.Token),
            callbackTarget,
            resolvedRequestId,
            onErrorCallback);
        return resolvedRequestId;
    }

    public string StartVoiceTurn(
        RuntimeEntity callbackTarget,
        string? requestId = null,
        float? timeoutSeconds = 30.0f,
        string? onTranscriptionCompletedCallback = "realtime_voice_transcription_completed",
        string? onDeltaCallback = "realtime_voice_delta",
        string? onCompletedCallback = "realtime_voice_completed",
        string? onTimeoutCallback = "realtime_voice_timeout",
        string? onErrorCallback = "realtime_voice_error")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);
        EnsureEnabled();
        EnsureMicrophoneInputAvailable();
        StopWakeWordMonitoring();

        string resolvedRequestId = ResolveRequestId(requestId);
        TrackRequest(async cts =>
        {
            RealtimeVoiceClient client = GetClient(cts.Token);
            await DeletePendingTranscribedUserItemBestEffortAsync(client, cts.Token).ConfigureAwait(false);

            RealtimeVoiceCaptureResult capture = await CaptureAndTranscribeAsync(
                timeoutSeconds,
                false,
                cts.Token).ConfigureAwait(false);
            if (capture.TimedOut)
            {
                DispatchEvent(
                    callbackTarget,
                    new RuntimeRealtimeVoiceScriptEvent(
                        resolvedRequestId,
                        "timeout",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        true,
                        string.Empty,
                        onTimeoutCallback ?? string.Empty,
                        string.Empty,
                        string.Empty));
                return;
            }

            OpenAiRealtimeTranscriptionResult? result = capture.Result;
            string userText = result?.Text?.Trim() ?? string.Empty;
            DispatchEvent(
                callbackTarget,
                new RuntimeRealtimeVoiceScriptEvent(
                    resolvedRequestId,
                    "transcription_completed",
                    userText,
                    string.Empty,
                    userText,
                    true,
                    string.Empty,
                    onTranscriptionCompletedCallback ?? string.Empty,
                    string.Empty,
                    userText));

            if (string.IsNullOrWhiteSpace(userText))
            {
                await DeleteConversationItemBestEffortAsync(client, result?.ItemId, cts.Token).ConfigureAwait(false);
                DispatchEvent(
                    callbackTarget,
                    new RuntimeRealtimeVoiceScriptEvent(
                        resolvedRequestId,
                        "completed",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        true,
                        string.Empty,
                        onCompletedCallback ?? string.Empty,
                        string.Empty,
                        string.Empty));
                return;
            }

            await RunResponseAsync(
                callbackTarget,
                userText,
                resolvedRequestId,
                onDeltaCallback,
                onCompletedCallback,
                cts.Token,
                result?.ItemId).ConfigureAwait(false);
        }, callbackTarget, resolvedRequestId, onErrorCallback);
        return resolvedRequestId;
    }

    public void CancelRequest(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _activeRequests.TryGetValue(requestId.Trim(), out CancellationTokenSource? found)
                ? found
                : null;
        }

        cts?.Cancel();
    }

    public void CancelAllRequests()
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

    public string StartSpeakText(
        RuntimeEntity callbackTarget,
        string text,
        float? speed = null,
        string? requestId = null,
        string? onCompletedCallback = "realtime_voice_speech_completed",
        string? onErrorCallback = "realtime_voice_error")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callbackTarget);
        EnsureEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        StopWakeWordMonitoring();

        string resolvedRequestId = ResolveRequestId(requestId);
        TrackRequest(async cts =>
        {
            AudioData audio = await GetClient(cts.Token).SynthesizeTextAsync(
                text.Trim(),
                new OpenAiAudioSpeechRequest
                {
                    Model = _settings.Model,
                    Voice = _settings.Voice,
                    Speed = speed is null ? _settings.PromptSpeed : Math.Clamp(speed.Value, 0.1f, 5.0f),
                    ResponseFormat = "wav"
                },
                cts.Token).ConfigureAwait(false);

            AudioData adjustedAudio = ApplyVolume(audio, _settings.OutputVolume);
            await StartLipSyncAsync(callbackTarget, text.Trim(), adjustedAudio.Duration).ConfigureAwait(false);
            try
            {
                await _audioPlayer.PlayAsync(adjustedAudio, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                await StopLipSyncAsync(callbackTarget).ConfigureAwait(false);
            }

            DispatchEvent(
                callbackTarget,
                new RuntimeRealtimeVoiceScriptEvent(
                    resolvedRequestId,
                    "speech_completed",
                    text.Trim(),
                    string.Empty,
                    text.Trim(),
                    true,
                    string.Empty,
                    onCompletedCallback ?? string.Empty,
                    string.Empty,
                    text.Trim()));
        }, callbackTarget, resolvedRequestId, onErrorCallback);
        return resolvedRequestId;
    }

    public Task ResetConversationAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        ClearPendingTranscribedUserItem();
        return GetClient(cancellationToken).ResetConversationAsync(cancellationToken);
    }

    internal void ClearScene()
    {
        StopWakeWordMonitoring();
        CancellationTokenSource[] requests;
        lock (_sync)
        {
            requests = _activeRequests.Values.ToArray();
            _activeRequests.Clear();
            _pendingTranscribedUserItem = null;
        }

        foreach (CancellationTokenSource request in requests)
        {
            request.Cancel();
            request.Dispose();
        }

        foreach (SpeechTransformUpdater updater in _speechUpdaters.Values)
        {
            updater.Stop(resetFace: true);
        }

        _speechUpdaters.Clear();
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
        _ownedAudioPlayer.Dispose();
        lock (_resourceSync)
        {
            _dictionaries = null;
        }
        _client?.Dispose();
        _client = null;
    }

    private async Task RunResponseAsync(
        RuntimeEntity callbackTarget,
        string userText,
        string requestId,
        string? onDeltaCallback,
        string? onCompletedCallback,
        CancellationToken cancellationToken,
        string? existingUserItemId = null)
    {
        RealtimeVoiceClient client = GetClient(cancellationToken);
        PendingRealtimeUserItem? pending = null;
        PendingRealtimeUserItem? stale = null;
        if (string.IsNullOrWhiteSpace(existingUserItemId))
        {
            pending = ConsumePendingTranscribedUserItem(userText, out stale);
            existingUserItemId = pending?.ItemId;
        }

        if (string.IsNullOrWhiteSpace(existingUserItemId))
        {
            await DeleteConversationItemBestEffortAsync(client, stale?.ItemId, cancellationToken).ConfigureAwait(false);
            await client.CreateUserTextConversationItemAsync(userText, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DeleteConversationItemBestEffortAsync(client, stale?.ItemId, cancellationToken).ConfigureAwait(false);
        }

        string accumulatedText = string.Empty;
        string finalText = string.Empty;
        Channel<AudioChunk> audioChannel = Channel.CreateUnbounded<AudioChunk>();
        Task? playbackTask = null;
        bool lipSyncLoopStarted = false;
        int lastLipSyncLength = 0;
        bool completed = false;

        try
        {
            await foreach (OpenAiRealtimeResponseUpdate update in client.CreateResponseAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(update.AssistantTranscript))
                {
                    accumulatedText = update.AssistantTranscript;
                }

                if (!string.IsNullOrEmpty(update.TranscriptDelta) || !string.IsNullOrWhiteSpace(update.AssistantTranscript))
                {
                    if (lipSyncLoopStarted && accumulatedText.Length - lastLipSyncLength >= 8)
                    {
                        lastLipSyncLength = accumulatedText.Length;
                        await StartLipSyncLoopAsync(callbackTarget, accumulatedText).ConfigureAwait(false);
                    }

                    DispatchEvent(
                        callbackTarget,
                        new RuntimeRealtimeVoiceScriptEvent(
                            requestId,
                            "delta",
                            userText,
                            update.TranscriptDelta ?? string.Empty,
                            accumulatedText,
                            false,
                            string.Empty,
                            onDeltaCallback ?? string.Empty,
                            string.Empty,
                            userText));
                }

                if (update.AudioChunk is not null)
                {
                    if (playbackTask is null)
                    {
                        playbackTask = _audioPlayer.PlayAsync(ReadAudioAsync(audioChannel.Reader, cancellationToken), update.AudioChunk.Format, cancellationToken);
                    }

                    if (!lipSyncLoopStarted)
                    {
                        lipSyncLoopStarted = true;
                        lastLipSyncLength = accumulatedText.Length;
                        string lipSyncText = !string.IsNullOrWhiteSpace(accumulatedText) ? accumulatedText : userText;
                        await StartLipSyncLoopAsync(callbackTarget, lipSyncText).ConfigureAwait(false);
                    }

                    await audioChannel.Writer.WriteAsync(ApplyVolume(update.AudioChunk, _settings.OutputVolume), cancellationToken).ConfigureAwait(false);
                }

                if (update.IsCompleted)
                {
                    finalText = string.IsNullOrWhiteSpace(update.FinalAssistantText)
                        ? accumulatedText.Trim()
                        : update.FinalAssistantText.Trim();
                    completed = true;
                    break;
                }
            }
        }
        finally
        {
            audioChannel.Writer.TryComplete();
            if (playbackTask is not null)
            {
                await playbackTask.ConfigureAwait(false);
            }

            if (lipSyncLoopStarted)
            {
                await StopLipSyncAsync(callbackTarget).ConfigureAwait(false);
            }
        }

        if (completed)
        {
            DispatchEvent(
                callbackTarget,
                new RuntimeRealtimeVoiceScriptEvent(
                    requestId,
                    "completed",
                    finalText,
                    string.Empty,
                    finalText,
                    true,
                    string.Empty,
                    onCompletedCallback ?? string.Empty,
                    string.Empty,
                    userText));
        }
    }

    private void RememberPendingTranscribedUserItem(OpenAiRealtimeTranscriptionResult? result)
    {
        PendingRealtimeUserItem? pending = null;
        if (!string.IsNullOrWhiteSpace(result?.ItemId) && !string.IsNullOrWhiteSpace(result.Text))
        {
            pending = new PendingRealtimeUserItem(result.ItemId, result.Text.Trim());
        }

        lock (_sync)
        {
            _pendingTranscribedUserItem = pending;
        }
    }

    private PendingRealtimeUserItem? ConsumePendingTranscribedUserItem(string userText, out PendingRealtimeUserItem? stale)
    {
        lock (_sync)
        {
            PendingRealtimeUserItem? pending = _pendingTranscribedUserItem;
            _pendingTranscribedUserItem = null;
            if (pending is null)
            {
                stale = null;
                return null;
            }

            if (string.Equals(NormalizeRealtimeUserText(pending.Value.Text), NormalizeRealtimeUserText(userText), StringComparison.Ordinal))
            {
                stale = null;
                return pending;
            }

            stale = pending;
            return null;
        }
    }

    private void ClearPendingTranscribedUserItem()
    {
        lock (_sync)
        {
            _pendingTranscribedUserItem = null;
        }
    }

    private async Task DeletePendingTranscribedUserItemBestEffortAsync(
        RealtimeVoiceClient client,
        CancellationToken cancellationToken)
    {
        PendingRealtimeUserItem? stale;
        lock (_sync)
        {
            stale = _pendingTranscribedUserItem;
            _pendingTranscribedUserItem = null;
        }

        await DeleteConversationItemBestEffortAsync(client, stale?.ItemId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteConversationItemBestEffortAsync(
        RealtimeVoiceClient client,
        string? itemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        try
        {
            await client.DeleteConversationItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Some Realtime-compatible servers may not retain/delete synthetic items reliably.
        }
    }

    private static string NormalizeRealtimeUserText(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private void TrackRequest(
        Func<CancellationTokenSource, Task> work,
        RuntimeEntity callbackTarget,
        string requestId,
        string? onErrorCallback)
    {
        CancellationTokenSource cts = new();
        lock (_sync)
        {
            _activeRequests[requestId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await work(cts).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested || _disposed)
            {
            }
            catch (Exception ex)
            {
                DispatchEvent(
                    callbackTarget,
                    new RuntimeRealtimeVoiceScriptEvent(
                        requestId,
                        "error",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        true,
                        ex.Message,
                        onErrorCallback ?? string.Empty,
                        string.Empty,
                        string.Empty));
            }
            finally
            {
                lock (_sync)
                {
                    _activeRequests.Remove(requestId);
                }

                cts.Dispose();
            }
        });
    }

    private async Task<RealtimeVoiceCaptureResult> CaptureAndTranscribeAsync(
        float? timeoutSeconds,
        bool deleteConversationItem,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds is > 0.0f)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
        }

        VoiceActivityCaptureOptions options = CreateVoiceActivityCaptureOptions(_settings.UserCapture);
        AudioData audio;
        try
        {
            audio = await _audioSource.RecordUntilSilenceAsync(options, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new RealtimeVoiceCaptureResult(null, TimedOut: true);
        }

        if (audio.Samples.Length == 0)
        {
            bool timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            return new RealtimeVoiceCaptureResult(null, TimedOut: timedOut);
        }

        OpenAiRealtimeTranscriptionResult result = await GetClient(cancellationToken).TranscribeAsync(audio, deleteConversationItem, cancellationToken).ConfigureAwait(false);
        return new RealtimeVoiceCaptureResult(result, TimedOut: false);
    }

    private async Task<RealtimeVoiceCaptureResult> CaptureAndTranscribeUntilTextAsync(
        float? timeoutSeconds,
        bool deleteConversationItem,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds is not > 0.0f)
        {
            RealtimeVoiceCaptureResult capture = await CaptureAndTranscribeAsync(
                timeoutSeconds,
                deleteConversationItem,
                cancellationToken).ConfigureAwait(false);
            if (!deleteConversationItem && string.IsNullOrWhiteSpace(capture.Result?.Text))
            {
                await DeleteConversationItemBestEffortAsync(GetClient(cancellationToken), capture.Result?.ItemId, cancellationToken).ConfigureAwait(false);
            }

            return capture;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds.Value);
        while (true)
        {
            double remainingSeconds = (deadline - DateTimeOffset.UtcNow).TotalSeconds;
            if (remainingSeconds <= 0.0)
            {
                return new RealtimeVoiceCaptureResult(null, TimedOut: true);
            }

            RealtimeVoiceCaptureResult capture = await CaptureAndTranscribeAsync(
                (float)remainingSeconds,
                deleteConversationItem,
                cancellationToken).ConfigureAwait(false);
            if (capture.TimedOut)
            {
                return capture;
            }

            if (!string.IsNullOrWhiteSpace(capture.Result?.Text))
            {
                return capture;
            }

            if (!deleteConversationItem)
            {
                await DeleteConversationItemBestEffortAsync(GetClient(cancellationToken), capture.Result?.ItemId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<OpenAiRealtimeTranscriptionResult?> CaptureAndRecognizeWakeWordAsync(
        ContinuousAudioCaptureSession captureSession,
        CancellationToken cancellationToken)
    {
        AudioData audio = await captureSession.ReadAsync(
            TimeSpan.FromSeconds(Math.Max(0.1f, _settings.WakeWord.ChunkDurationSeconds)),
            cancellationToken,
            discardBufferedAudio: true).ConfigureAwait(false);

        if (audio.Samples.Length == 0)
        {
            return null;
        }

        if (CalculateRms(audio.Samples) < WakeWordSpeechRmsThreshold)
        {
            return null;
        }

        AudioData padded = AppendTrailingSilence(audio, TimeSpan.FromSeconds(Math.Max(0.0f, _settings.WakeWord.TrailingSilencePaddingSeconds)));
        OpenAiRealtimeTranscriptionResult result = await GetClient(cancellationToken).TranscribeAsync(padded, deleteConversationItem: true, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.Text) || !LooksLikeWakeWordPrefix(result.Text))
        {
            return string.IsNullOrWhiteSpace(result.Text) ? null : result;
        }

        AudioData extension = await captureSession.ReadAsync(
            TimeSpan.FromSeconds(Math.Max(0.0f, _settings.WakeWord.ExtensionDurationSeconds)),
            cancellationToken).ConfigureAwait(false);
        if (extension.Samples.Length == 0)
        {
            return result;
        }

        AudioData combined = ConcatAudio(audio, extension);
        AudioData combinedPadded = AppendTrailingSilence(combined, TimeSpan.FromSeconds(Math.Max(0.0f, _settings.WakeWord.TrailingSilencePaddingSeconds)));
        return await GetClient(cancellationToken).TranscribeAsync(combinedPadded, deleteConversationItem: true, cancellationToken).ConfigureAwait(false);
    }

    private static float CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0f;
        }

        double sumSquares = 0.0;
        foreach (float sample in samples)
        {
            sumSquares += sample * sample;
        }

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    private RealtimeVoiceClient GetClient(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_client is not null)
        {
            return _client;
        }

        _client = new RealtimeVoiceClient(
            new RealtimeVoiceClientSettings
            {
                ClientOptions = new OpenAiRealtimeClientOptions
                {
                    BaseUrl = _settings.BaseUrl,
                    RealtimePath = _settings.RealtimePath,
                    AudioSpeechPath = _settings.AudioSpeechPath,
                    ApiKey = ResolveApiKey(),
                    Model = _settings.Model,
                    ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(_settings.ConnectTimeoutSeconds, 1, 3600)),
                    OutboundAudioChunkSamples = Math.Max(512, _settings.OutboundAudioChunkSamples)
                },
                Session = new OpenAiRealtimeSession
                {
                    Model = _settings.Model,
                    Instructions = _settings.Instructions,
                    OutputModalities = ["audio"],
                    Audio = new OpenAiRealtimeSessionAudioOptions
                    {
                        Input = new OpenAiRealtimeSessionInputAudioOptions
                        {
                            Format = OpenAiRealtimeAudioFormat.Pcm16(_settings.InputAudioSampleRate),
                            Transcription = new OpenAiRealtimeInputAudioTranscription
                            {
                                Model = _settings.InputTranscriptionModel,
                                Language = _settings.InputTranscriptionLanguage,
                                Prompt = string.IsNullOrWhiteSpace(_settings.InputTranscriptionPrompt)
                                    ? null
                                    : _settings.InputTranscriptionPrompt
                            },
                            TurnDetection = null
                        },
                        Output = new OpenAiRealtimeSessionOutputAudioOptions
                        {
                            Format = OpenAiRealtimeAudioFormat.Pcm16(_settings.OutputAudioSampleRate),
                            Voice = _settings.Voice
                        }
                    },
                    MaxOutputTokens = _settings.MaxOutputTokens,
                    Temperature = _settings.Temperature
                }
            },
            NullLogger<RealtimeVoiceClient>.Instance);
        _client.EnsureReadyAsync(cancellationToken).GetAwaiter().GetResult();
        return _client;
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return _settings.ApiKey;
        }

        return string.IsNullOrWhiteSpace(_settings.ApiKeyEnvironmentVariable)
            ? string.Empty
            : Environment.GetEnvironmentVariable(_settings.ApiKeyEnvironmentVariable) ?? string.Empty;
    }

    private void EnsureEnabled()
    {
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Project Realtime Voice is disabled. Enable Project.RealtimeVoice.Enabled in GameEditor or game.project.json.");
        }

    }

    private void EnsureMicrophoneInputAvailable()
    {
        if (!_microphoneInputAvailable)
        {
            throw new InvalidOperationException($"Project Realtime Voice microphone input is unavailable: {_microphoneUnavailableReason}");
        }
    }

    private static string ResolveRequestId(string? requestId)
    {
        return string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : requestId.Trim();
    }

    private void AttachEntity(RuntimeEntity entity)
    {
        if (!_voiceSettings.LipSync.Enabled || !entity.IsPmxModel)
        {
            return;
        }

        if (_speechUpdaters.ContainsKey(entity.Id))
        {
            return;
        }

        SpeechDictionarySet dictionaries = EnsureSpeechDictionaries();
        SpeechTransformUpdater updater = entity.CreateSpeechUpdater(
            dictionaries,
            _voiceSettings.LipSync.VowelMorphMap,
            ResolveNoMatchFallbackVowel(_voiceSettings.LipSync));
        updater.Stop(resetFace: true);
        _speechUpdaters[entity.Id] = updater;
    }

    private async Task StartLipSyncAsync(RuntimeEntity entity, string text, TimeSpan audioDuration)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            AttachEntity(entity);
            if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? updater))
            {
                updater.Start(text, CalculateFramePeriod(updater, text, audioDuration), isLoop: false);
            }
        }).ConfigureAwait(false);
    }

    private async Task StartLipSyncLoopAsync(RuntimeEntity entity, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            AttachEntity(entity);
            if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? updater))
            {
                updater.Start(text, TimeSpan.FromMilliseconds(150.0), isLoop: true);
            }
        }).ConfigureAwait(false);
    }

    private async Task StopLipSyncAsync(RuntimeEntity entity)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (_speechUpdaters.TryGetValue(entity.Id, out SpeechTransformUpdater? updater))
            {
                updater.Stop(resetFace: true);
            }
        }).ConfigureAwait(false);
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
                    double fallbackMilliseconds = Math.Max(
                        Math.Max(1.0, audioDuration.TotalMilliseconds),
                        Math.Max(1.0f, _voiceSettings.LipSync.MinFramePeriodMilliseconds));
                    fallbackMilliseconds = Math.Min(fallbackMilliseconds, Math.Max(_voiceSettings.LipSync.MinFramePeriodMilliseconds, _voiceSettings.LipSync.MaxFramePeriodMilliseconds));
                    return TimeSpan.FromMilliseconds(fallbackMilliseconds);
                }

                return TimeSpan.FromMilliseconds(180.0);
            }

            double targetDurationMilliseconds = Math.Max(1.0, audioDuration.TotalMilliseconds);
            double milliseconds = Math.Max(
                targetDurationMilliseconds / vowelCount,
                Math.Max(1.0f, _voiceSettings.LipSync.MinFramePeriodMilliseconds));
            milliseconds = Math.Min(milliseconds, Math.Max(_voiceSettings.LipSync.MinFramePeriodMilliseconds, _voiceSettings.LipSync.MaxFramePeriodMilliseconds));
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

    private SpeechDictionarySet EnsureSpeechDictionaries()
    {
        lock (_resourceSync)
        {
            if (_dictionaries is not null)
            {
                return _dictionaries;
            }

            string dictionaryDirectory = ResolveOptionalPath(_voiceSettings.LipSync.DictionaryDirectory)
                ?? Path.Combine(AppContext.BaseDirectory, "Resources", "SpeechLipSyncDictionaries");
            _dictionaries = SpeechDictionarySet.LoadFromDirectory(
                dictionaryDirectory,
                ResolveDictionaryLanguages(_voiceSettings.LipSync));
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

    private void DispatchEvent(RuntimeEntity target, RuntimeRealtimeVoiceScriptEvent scriptEvent)
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

    private VoiceActivityCaptureOptions CreateVoiceActivityCaptureOptions(GameProjectVoiceActivityCaptureSettings capture)
    {
        return new VoiceActivityCaptureOptions
        {
            DeviceIndex = _settings.InputDeviceIndex,
            SampleRate = Math.Max(1, capture.SampleRate),
            Channels = Math.Max(1, capture.Channels),
            FramesPerBuffer = (uint)Math.Max(0, capture.FramesPerBuffer),
            PreRoll = TimeSpan.FromSeconds(Math.Max(0.0f, capture.PreRollSeconds)),
            MinDuration = TimeSpan.FromSeconds(Math.Max(0.0f, capture.MinDurationSeconds)),
            MaxDuration = TimeSpan.FromSeconds(Math.Max(0.1f, capture.MaxDurationSeconds)),
            SilenceTimeout = TimeSpan.FromSeconds(Math.Max(0.0f, capture.SilenceTimeoutSeconds)),
            SilenceThreshold = Math.Clamp(capture.SilenceThreshold, 0.0f, 1.0f)
        };
    }

    private AudioCaptureOptions CreateAudioCaptureOptions(GameProjectAudioCaptureSettings capture)
    {
        return new AudioCaptureOptions
        {
            DeviceIndex = _settings.InputDeviceIndex,
            SampleRate = Math.Max(1, capture.SampleRate),
            Channels = Math.Max(1, capture.Channels),
            FramesPerBuffer = (uint)Math.Max(0, capture.FramesPerBuffer)
        };
    }

    private static async IAsyncEnumerable<AudioChunk> ReadAudioAsync(
        ChannelReader<AudioChunk> reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (AudioChunk chunk in reader.ReadAllAsync(cancellationToken))
        {
            yield return chunk;
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
        for (int i = 0; i < chunk.Samples.Length; i++)
        {
            scaled[i] = Math.Clamp(chunk.Samples.Span[i] * volume, -1.0f, 1.0f);
        }

        return new AudioChunk(scaled, chunk.Format, chunk.Offset, chunk.IsFinal);
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

    private static AudioData ConcatAudio(AudioData first, AudioData second)
    {
        AudioData normalizedSecond = first.Format.SampleRate == second.Format.SampleRate && first.Format.Channels == second.Format.Channels
            ? second
            : second.ToMono().Resample(first.Format.SampleRate);

        float[] combined = new float[first.Samples.Length + normalizedSecond.Samples.Length];
        Array.Copy(first.Samples, combined, first.Samples.Length);
        Array.Copy(normalizedSecond.Samples, 0, combined, first.Samples.Length, normalizedSecond.Samples.Length);
        return new AudioData(combined, first.Format);
    }

    private bool TryExtractWakeWordTail(string text, out string? matchedWakeWord, out string? tailText)
    {
        foreach (string wakeWord in _settings.WakeWord.Keywords)
        {
            string normalizedWakeWord = NormalizeWakeWordText(wakeWord);
            string normalizedText = NormalizeWakeWordText(text);
            if (string.IsNullOrWhiteSpace(normalizedWakeWord)
                || !normalizedText.Contains(normalizedWakeWord, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int index = normalizedText.IndexOf(normalizedWakeWord, StringComparison.OrdinalIgnoreCase);
            string tail = index < 0
                ? string.Empty
                : normalizedText[(index + normalizedWakeWord.Length)..].Trim();
            matchedWakeWord = wakeWord;
            tailText = tail;
            return true;
        }

        matchedWakeWord = null;
        tailText = null;
        return false;
    }

    private bool LooksLikeWakeWordPrefix(string recognizedText)
    {
        string normalizedRecognized = NormalizeWakeWordText(recognizedText);
        if (string.IsNullOrWhiteSpace(normalizedRecognized))
        {
            return false;
        }

        foreach (string wakeWord in _settings.WakeWord.Keywords)
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

    private readonly record struct PendingRealtimeUserItem(string ItemId, string Text);

    private readonly record struct RealtimeVoiceCaptureResult(OpenAiRealtimeTranscriptionResult? Result, bool TimedOut);
}
