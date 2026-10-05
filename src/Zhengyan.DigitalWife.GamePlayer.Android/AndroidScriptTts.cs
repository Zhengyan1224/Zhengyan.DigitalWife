using Android.App;
using Android.OS;
using Android.Speech.Tts;
using Java.Util;
using System.Collections.Concurrent;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptTts : Java.Lang.Object, IAndroidScriptTts
{
    private static readonly Lazy<AndroidScriptTts> LazyShared = new(() => new());
    private TextToSpeech? _engine;
    private readonly Handler _main = new(Looper.MainLooper!);
    private readonly AndroidTtsInitialization _initialization = new(TimeSpan.FromSeconds(10));
    private TaskCompletionSource<bool>? _pendingInitialization;
    private InitializationListener? _initializationListener;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _utterances = new();
    private readonly ConcurrentDictionary<string, PlaybackCallbacks> _playbackCallbacks = new();
    private readonly CompletionListener _listener;
    private string? _activeUtterance;
    private int _stopVersion;
    private volatile bool _disposed;

    private AndroidScriptTts()
    {
        _listener = new CompletionListener(this);
        _ = WarmUpAsync();
    }

    public static AndroidScriptTts Shared => LazyShared.Value;
    public bool IsReady => !_disposed && _initialization.IsReady;

    private async Task WarmUpAsync()
    {
        try { await _initialization.EnsureReadyAsync(InitializeEngineAsync).ConfigureAwait(false); }
        catch (Exception ex) { global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"TTS initialization: {ex.Message}"); }
    }

    private Task InitializeEngineAsync()
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_main.Post(() =>
        {
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ReleaseEngine();
                _pendingInitialization = completion;
                // Some engines report failure during the constructor. Posting the
                // callback ensures _engine is assigned before inspecting it, and
                // the attempt identity rejects late callbacks from a retired engine.
                _initializationListener = new InitializationListener(status =>
                    _main.Post(() => FinishInitialization(completion, status)));
                _engine = new TextToSpeech(Application.Context, _initializationListener);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new InvalidOperationException("Android TTS main looper is unavailable."));
        return completion.Task;
    }

    private void FinishInitialization(TaskCompletionSource<bool> completion, OperationResult status)
    {
        if (_disposed || !ReferenceEquals(completion, _pendingInitialization) || completion.Task.IsCompleted) return;
        try
        {
            string engines = string.Join(", ", _engine?.Engines?.Select(engine => engine.Name) ?? []);
            string defaultEngine = _engine?.DefaultEngine ?? "<none>";
            if (status != OperationResult.Success || _engine is null)
                throw new InvalidOperationException(string.IsNullOrEmpty(engines)
                    ? "No Android TTS engine is visible. Enable or install a text-to-speech engine in Android settings."
                    : $"Android TTS initialization failed (status={status}, default={defaultEngine}, engines={engines}). The next speech request will retry.");
            if ((int)_engine.SetOnUtteranceProgressListener(_listener) != 0)
                throw new InvalidOperationException("Android TTS could not register playback completion callbacks.");
            global::Android.Util.Log.Info("ZhengyanGamePlayer", $"TTS ready: default={defaultEngine}; engines={engines}");
            completion.TrySetResult(true);
        }
        catch (Exception ex) { completion.TrySetException(ex); }
    }

    public bool Speak(string text, string? language = null, bool flush = true)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return false;
        if (!IsReady || _engine is null) { _ = WarmUpAsync(); return false; }
        if (!string.IsNullOrWhiteSpace(language) &&
            _engine.SetLanguage(Locale.ForLanguageTag(language)) is LanguageAvailableResult.MissingData or LanguageAvailableResult.NotSupported)
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android TTS language is unavailable: {language}");
            return false;
        }
        if (flush) CancelUtterances();
        bool started = _engine.Speak(text, flush ? QueueMode.Flush : QueueMode.Add, null, Guid.NewGuid().ToString("N")) == OperationResult.Success;
        if (!started) _initialization.Invalidate();
        return started;
    }

    public Task SpeakAsync(string text, float speed = 1.0f, float volume = 1.0f, CancellationToken cancellationToken = default)
        => SpeakWithProgressAsync(text, speed, volume, null, null, cancellationToken);

    internal async Task SpeakWithProgressAsync(string text, float speed, float volume,
        Action? started, Action<int, int>? rangeStarted, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        int stopVersion = Volatile.Read(ref _stopVersion);
        await _initialization.EnsureReadyAsync(InitializeEngineAsync, cancellationToken).ConfigureAwait(false);
        string id = Guid.NewGuid().ToString("N");
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_main.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested || _disposed || stopVersion != Volatile.Read(ref _stopVersion))
            { completion.TrySetCanceled(); return; }
            CancelUtterances();
            _utterances[id] = completion;
            _playbackCallbacks[id] = new(started, rangeStarted);
            _activeUtterance = id;
            try
            {
                if (!IsReady || _engine is null) throw new InvalidOperationException("Android TTS engine is no longer ready; retry speech.");
                _engine.SetSpeechRate(Math.Clamp(speed, 0.1f, 4.0f));
                using Bundle parameters = new();
                parameters.PutFloat(TextToSpeech.Engine.KeyParamVolume, Math.Clamp(volume, 0.0f, 1.0f));
                if (_engine.Speak(text, QueueMode.Flush, parameters, id) != OperationResult.Success)
                {
                    _initialization.Invalidate();
                    Complete(id, new InvalidOperationException("Android text-to-speech could not start."));
                }
            }
            catch (Exception ex) { Complete(id, ex); }
        })) completion.TrySetException(new InvalidOperationException("Android TTS main looper is unavailable."));
        try { await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            _utterances.TryRemove(id, out _);
            _playbackCallbacks.TryRemove(id, out _);
            if (cancellationToken.IsCancellationRequested)
                _main.Post(() => { if (_activeUtterance == id) { _engine?.Stop(); _activeUtterance = null; } });
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _stopVersion);
        CancelUtterances();
        _main.Post(() => { _engine?.Stop(); _activeUtterance = null; });
    }

    private void CancelUtterances()
    {
        foreach (string id in _utterances.Keys)
            if (_utterances.TryRemove(id, out var completion))
            {
                _playbackCallbacks.TryRemove(id, out _);
                completion.TrySetCanceled();
            }
    }

    private void Complete(string? id, Exception? error = null)
    {
        if (id is null || !_utterances.TryRemove(id, out var completion)) return;
        _playbackCallbacks.TryRemove(id, out _);
        if (error is null) completion.TrySetResult(true);
        else completion.TrySetException(error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            Stop();
            _initialization.Invalidate();
            _main.Post(() => { ReleaseEngine(); _listener.Dispose(); });
        }
        base.Dispose(disposing);
    }

    private void ReleaseEngine()
    {
        _pendingInitialization?.TrySetCanceled();
        _pendingInitialization = null;
        _engine?.Shutdown();
        _engine?.Dispose();
        _engine = null;
        _initializationListener?.Dispose();
        _initializationListener = null;
    }

    private sealed class InitializationListener(Action<OperationResult> initialized) : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status) => initialized(status);
    }

    private sealed class CompletionListener(AndroidScriptTts owner) : UtteranceProgressListener
    {
        public override void OnStart(string? utteranceId)
        {
            if (utteranceId is not null && owner._playbackCallbacks.TryGetValue(utteranceId, out var callbacks))
                callbacks.Started?.Invoke();
        }
        public override void OnRangeStart(string? utteranceId, int start, int end, int frame)
        {
            if (utteranceId is not null && owner._playbackCallbacks.TryGetValue(utteranceId, out var callbacks))
                callbacks.RangeStarted?.Invoke(start, end);
        }
        public override void OnDone(string? utteranceId) => owner.Complete(utteranceId);
        [Obsolete("Required override for the Android UtteranceProgressListener contract.")]
        public override void OnError(string? utteranceId) => owner.Complete(utteranceId, new InvalidOperationException("Android text-to-speech failed."));
        public override void OnStop(string? utteranceId, bool interrupted)
        {
            if (utteranceId is not null && owner._utterances.TryRemove(utteranceId, out var completion))
            {
                owner._playbackCallbacks.TryRemove(utteranceId, out _);
                completion.TrySetCanceled();
            }
        }
    }

    private sealed record PlaybackCallbacks(Action? Started, Action<int, int>? RangeStarted);
}
