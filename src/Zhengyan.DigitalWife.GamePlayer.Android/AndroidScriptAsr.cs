using Android.App;
using Android.Content;
using Android.OS;
using Android.Speech;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptAsr : Java.Lang.Object, IRecognitionListener, IAndroidScriptAsr
{
    private static readonly Lazy<AndroidScriptAsr> LazyShared = new(() => new());
    private const int MaxHandoffRetries = 3;
    private readonly object _sync = new();
    private readonly ILogger _logger = AndroidRuntimeLoggerFactory.Instance.CreateLogger(nameof(AndroidScriptAsr));
    private readonly Handler _main = new(Looper.MainLooper!);
    private SpeechRecognizer? _recognizer;
    private bool _listening;
    private bool _monitoring;
    private bool _streaming;
    private bool _stopRequested;
    private bool _nativeActive;
    private int _generation;
    private int _requestGeneration;
    private int _handoffRetries;
    private int _networkRetryDelayMilliseconds;
    private bool _readyForSpeech;
    private bool _speechDetected;
    private bool _needsHandoffDelay;
    private Action<AndroidRuntimeEvent>? _dispatchEvent;
    private Action<Action>? _postToScriptThread;
    private string _requestId = string.Empty;
    private string _entityId = string.Empty;
    private string _partialCallback = string.Empty;
    private string _completedCallback = string.Empty;
    private string _errorCallback = string.Empty;
    private string _eventType = "asr";
    private string _language = string.Empty;
    private string[] _wakeWords = [];
    private float? _silenceSeconds;

    private AndroidScriptAsr() { }
    public static AndroidScriptAsr Shared => LazyShared.Value;
    public bool Enabled => IsAvailable;
    public bool IsAvailable => SpeechRecognizer.IsRecognitionAvailable(Application.Context);
    public bool IsListening { get { lock (_sync) return _listening; } }
    public bool IsWakeWordMonitoring { get { lock (_sync) return _monitoring; } }
    public string LastText { get; private set; } = string.Empty;
    public event Action<string>? PartialResult;
    public event Action<string>? Result;
    public event Action<int>? Error;
    public void ConfigureCallbacks(Action<AndroidRuntimeEvent> dispatchEvent, Action<Action>? postToScriptThread = null)
    {
        lock (_sync) { _dispatchEvent = dispatchEvent; _postToScriptThread = postToScriptThread; }
    }

    public string StartStreamingRecognition(AndroidScriptEntity entity, string? requestId = null,
        string? onPartialCallback = "asr_partial", string? onCompletedCallback = "asr_completed",
        string? onErrorCallback = "asr_error", string eventType = "asr")
    {
        lock (_sync)
        {
            Cancel();
            _requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            _entityId = entity.Id;
            _partialCallback = onPartialCallback ?? string.Empty;
            _completedCallback = onCompletedCallback ?? string.Empty;
            _errorCallback = onErrorCallback ?? string.Empty;
            _eventType = eventType;
            _silenceSeconds = null;
            LastText = string.Empty;
            _streaming = true;
            return Start() ? _requestId : string.Empty;
        }
    }

    public string StartWakeWordMonitoring(AndroidScriptEntity entity, IEnumerable<string> wakeWords,
        string? requestId = null, float? chunkDurationSeconds = null, float? extensionDurationSeconds = null,
        float? trailingSilencePaddingSeconds = null, string? onDetectedCallback = "asr_wake_word_detected",
        string? onErrorCallback = "asr_wake_word_error")
    {
        lock (_sync)
        {
            string[] words = wakeWords.Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (words.Length == 0) throw new ArgumentException("At least one wake word is required.", nameof(wakeWords));
            Cancel();
            _requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            _entityId = entity.Id;
            _partialCallback = string.Empty;
            _completedCallback = onDetectedCallback ?? string.Empty;
            _errorCallback = onErrorCallback ?? string.Empty;
            _eventType = "asr";
            _wakeWords = words;
            _monitoring = true;
            LastText = string.Empty;
            // Android's recognizer owns capture chunks. Forward its supported silence
            // hint; the desktop chunk/extension parameters remain source compatible.
            _silenceSeconds = trailingSilencePaddingSeconds;
            return Start() ? _requestId : string.Empty;
        }
    }

    public void StopWakeWordMonitoring() { lock (_sync) { if (_monitoring) Cancel(); } }
    public void StopStreamingRecognition(string requestId) { lock (_sync) { if (_streaming && requestId == _requestId) Stop(); } }

    public bool Start(string language = "")
    {
        lock (_sync)
        {
            if (!IsAvailable)
            {
                _listening = _monitoring = _streaming = false;
                _logger.LogError("ASR unavailable request={RequestId}, callback={Callback}.", _requestId, _errorCallback);
                Dispatch("error", _errorCallback, error: "Android speech recognition is unavailable.", isFinal: true);
                return false;
            }
            _language = language;
            _stopRequested = false;
            _handoffRetries = 0;
            _networkRetryDelayMilliseconds = 0;
            int generation = ++_generation;
            _listening = true;
            _logger.LogInformation("ASR start request={RequestId}, mode={Mode}, generation={Generation}.",
                _requestId, _monitoring ? "wake" : _streaming ? "question" : "single", generation);
            // Allow the recognition service to release capture after cancel/destroy.
            _main.PostDelayed(() => StartNative(generation), _needsHandoffDelay ? 300 : 0);
            return true;
        }
    }

    private void StartNative(int generation)
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            if (_streaming && _stopRequested)
            {
                CompleteStreaming(LastText);
                return;
            }
            try
            {
                ReleaseNative(_recognizer);
                _recognizer = null;
                _needsHandoffDelay = false;
                _readyForSpeech = false;
                _speechDetected = false;
                _recognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context)
                    ?? throw new InvalidOperationException("Could not create Android speech recognizer.");
                _recognizer.SetRecognitionListener(new SessionListener(this, generation));
                Intent intent = new(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
                intent.PutExtra(RecognizerIntent.ExtraLanguage, string.IsNullOrWhiteSpace(_language) ? Java.Util.Locale.Default!.ToString() : _language);
                if (_silenceSeconds.HasValue)
                    intent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, (long)(Math.Clamp(_silenceSeconds.Value, 0.1f, 10.0f) * 1000));
                _nativeActive = true;
                _logger.LogInformation("ASR native start request={RequestId}, generation={Generation}, handoffRetries={Retries}.",
                    _requestId, generation, _handoffRetries);
                _recognizer.StartListening(intent);
            }
            catch (Exception ex)
            {
                _listening = _monitoring = _streaming = _nativeActive = false;
                ++_generation;
                _logger.LogError(ex, "ASR native start failed request={RequestId}, callback={Callback}.", _requestId, _errorCallback);
                Dispatch("error", _errorCallback, error: ex.Message, isFinal: true);
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _monitoring = false;
            _stopRequested = true;
            int generation = _generation;
            _main.Post(() =>
            {
                lock (_sync)
                {
                    if (generation != _generation) return;
                    if (_nativeActive) _recognizer?.StopListening();
                    // The script's deadline can fall between native recognition sessions.
                    // Complete it now instead of restarting or leaving it waiting forever.
                    else if (_streaming) CompleteStreaming(LastText);
                }
            });
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            ++_generation;
            ++_requestGeneration;
            _needsHandoffDelay |= _recognizer is not null;
            SpeechRecognizer? recognizer = _recognizer;
            _recognizer = null;
            _monitoring = _listening = _streaming = _nativeActive = _stopRequested = false;
            // Release exactly the canceled instance; a queued cleanup must never
            // destroy a recognizer created for a newer script request.
            _main.Post(() => ReleaseNative(recognizer));
        }
    }

    private void ReleaseNative(SpeechRecognizer? recognizer)
    {
        if (recognizer is null) return;
        try { recognizer.Cancel(); }
        catch (Exception ex) { _logger.LogWarning(ex, "ASR native cancel failed."); }
        try { recognizer.Destroy(); }
        catch (Exception ex) { _logger.LogWarning(ex, "ASR native destroy failed."); }
    }

    public void OnResults(Bundle? results)
    {
        lock (_sync)
        {
            _listening = false;
            _nativeActive = false;
            string text = Extract(results);
            _handoffRetries = 0;
            _networkRetryDelayMilliseconds = 0;
            if (_monitoring)
            {
                LastText = text;
                string normalized = Normalize(LastText);
                string? matched = _wakeWords.FirstOrDefault(word => normalized.Contains(Normalize(word), StringComparison.OrdinalIgnoreCase));
                if (matched is not null)
                {
                    _monitoring = false;
                    ++_generation;
                    Dispatch("wake_word_detected", _completedCallback, LastText, isFinal: true, wakeWord: matched);
                }
                else RestartRecognition();
                return;
            }
            if (_streaming && !_stopRequested && string.IsNullOrWhiteSpace(text))
            {
                RestartRecognition();
                return;
            }
            CompleteStreaming(_streaming && _stopRequested && string.IsNullOrWhiteSpace(text) ? LastText : text);
        }
    }

    public void OnPartialResults(Bundle? results)
    {
        lock (_sync)
        {
            string text = Extract(results);
            if (text.Length == 0 || _monitoring) return;
            LastText = text;
            _readyForSpeech = true;
            _speechDetected = true;
            _handoffRetries = 0;
            PartialResult?.Invoke(text);
            Dispatch("partial", _partialCallback, text);
        }
    }

    public void OnError(SpeechRecognizerError error)
    {
        lock (_sync)
        {
            _listening = false;
            _nativeActive = false;
            if ((_monitoring || _streaming) && error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout)
            {
                _logger.LogInformation("ASR native silence request={RequestId}, error={Error}, code={Code}, stopRequested={StopRequested}.",
                    _requestId, error, (int)error, _stopRequested);
                _handoffRetries = 0;
                _networkRetryDelayMilliseconds = 0;
                // These end one Android recognition session, not the script's listening
                // window. Desktop keeps capturing until speech or an explicit stop.
                if (_streaming && _stopRequested) CompleteStreaming(LastText);
                else RestartRecognition();
                return;
            }
            // Android's default service may use a remote recognizer. A timeout
            // in background wake monitoring must not reset a reply in progress.
            // Some services also report their idle session end as NetworkTimeout;
            // keep an empty question open until the script's existing deadline.
            if ((_monitoring && error is SpeechRecognizerError.NetworkTimeout or SpeechRecognizerError.Network)
                || (_streaming && error == SpeechRecognizerError.NetworkTimeout && !_speechDetected && string.IsNullOrWhiteSpace(LastText)))
            {
                if (_streaming && _stopRequested) { CompleteStreaming(LastText); return; }
                _networkRetryDelayMilliseconds = _networkRetryDelayMilliseconds == 0
                    ? 500 : Math.Min(_networkRetryDelayMilliseconds * 2, 5000);
                _logger.LogWarning("ASR network recovery request={RequestId}, mode={Mode}, callback={Callback}, error={Error}, code={Code}, delayMs={Delay}.",
                    _requestId, _monitoring ? "wake" : "question", _errorCallback, error, (int)error, _networkRetryDelayMilliseconds);
                RestartRecognition(_networkRetryDelayMilliseconds);
                return;
            }
            // A service can still be releasing the reply's interrupt monitor when
            // the question starts. Keep this recovery inside the script's deadline.
            bool handoffError = error == SpeechRecognizerError.RecognizerBusy
                || (error == SpeechRecognizerError.Client && !_readyForSpeech);
            if ((_monitoring || _streaming) && handoffError)
            {
                if (_streaming && _stopRequested) { CompleteStreaming(LastText); return; }
                if (_handoffRetries < MaxHandoffRetries)
                {
                    ++_handoffRetries;
                    _logger.LogWarning("ASR recovering request={RequestId}, callback={Callback}, error={Error}, code={Code}, retry={Retry}/{Limit}.",
                        _requestId, _errorCallback, error, (int)error, _handoffRetries, MaxHandoffRetries);
                    RestartRecognition(500 * _handoffRetries);
                    return;
                }
            }
            _monitoring = _streaming = false;
            ++_generation;
            _logger.LogError("ASR failed request={RequestId}, callback={Callback}, error={Error}, code={Code}, ready={Ready}, speechDetected={SpeechDetected}, handoffRetries={Retries}.",
                _requestId, _errorCallback, error, (int)error, _readyForSpeech, _speechDetected, _handoffRetries);
            Error?.Invoke((int)error);
            Dispatch("error", _errorCallback, error: error.ToString(), isFinal: true);
        }
    }

    private void RestartRecognition(int delayMilliseconds = 300)
    {
        int generation = ++_generation;
        _listening = true;
        _main.PostDelayed(() =>
        {
            lock (_sync)
            {
                if ((_monitoring || _streaming) && generation == _generation) StartNative(generation);
            }
        }, delayMilliseconds);
    }

    private void CompleteStreaming(string text)
    {
        _listening = _streaming = _nativeActive = false;
        ++_generation;
        LastText = text;
        _logger.LogInformation("ASR completed request={RequestId}, callback={Callback}, textLength={TextLength}, stopRequested={StopRequested}.",
            _requestId, _completedCallback, text.Length, _stopRequested);
        Result?.Invoke(text);
        Dispatch("completed", _completedCallback, text, isFinal: true);
    }

    private void Dispatch(string eventName, string callback, string text = "", string error = "", bool isFinal = false, string wakeWord = "")
    {
        int requestGeneration = _requestGeneration;
        Action<AndroidRuntimeEvent>? dispatch = _dispatchEvent;
        AndroidRuntimeEvent value = new(_eventType, _requestId, eventName, Vector2.Zero, text, _entityId,
            Error: error, CallbackName: callback, IsFinal: isFinal, WakeWord: wakeWord, RecognizedText: text);
        void Deliver()
        {
            lock (_sync)
            {
                // Native callbacks and script events use different queues. Check
                // cancellation again when the script consumes the event.
                if (requestGeneration != _requestGeneration) return;
            }
            // Script execution may await work on another thread that calls ASR.
            // Never hold the native state lock while running the script.
            dispatch?.Invoke(value);
        }
        if (_postToScriptThread is { } post) post(Deliver);
        else Deliver();
    }

    private static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit));
    private static string Extract(Bundle? bundle) => bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() ?? string.Empty;
    public void OnBeginningOfSpeech() { lock (_sync) { _readyForSpeech = _speechDetected = true; _handoffRetries = 0; } }
    public void OnBufferReceived(byte[]? buffer) { }
    public void OnEndOfSpeech() { }
    public void OnEvent(int eventType, Bundle? @params) { }
    public void OnReadyForSpeech(Bundle? @params) { lock (_sync) { _readyForSpeech = true; _handoffRetries = 0; } }
    public void OnRmsChanged(float rmsdB) { }
    protected override void Dispose(bool disposing) { if (disposing) Cancel(); base.Dispose(disposing); }

    private sealed class SessionListener(AndroidScriptAsr owner, int generation) : Java.Lang.Object, IRecognitionListener
    {
        public void OnResults(Bundle? results) { lock (owner._sync) { if (generation == owner._generation) owner.OnResults(results); } }
        public void OnPartialResults(Bundle? results) { lock (owner._sync) { if (generation == owner._generation) owner.OnPartialResults(results); } }
        public void OnError(SpeechRecognizerError error) { lock (owner._sync) { if (generation == owner._generation) owner.OnError(error); } }
        public void OnBeginningOfSpeech() { lock (owner._sync) { if (generation == owner._generation) owner.OnBeginningOfSpeech(); } }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle? @params) { }
        public void OnReadyForSpeech(Bundle? @params) { lock (owner._sync) { if (generation == owner._generation) owner.OnReadyForSpeech(@params); } }
        public void OnRmsChanged(float rmsdB) { }
    }
}
