using Android.App;
using Android.Content;
using Android.OS;
using Android.Speech;
using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptAsr : Java.Lang.Object, IRecognitionListener, IAndroidScriptAsr
{
    private static readonly Lazy<AndroidScriptAsr> LazyShared = new(() => new());
    private readonly Handler _main = new(Looper.MainLooper!);
    private SpeechRecognizer? _recognizer;
    private bool _listening;
    private bool _monitoring;
    private int _generation;
    private Action<AndroidRuntimeEvent>? _dispatchEvent;
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
    public bool IsListening => _listening;
    public bool IsWakeWordMonitoring => _monitoring;
    public string LastText { get; private set; } = string.Empty;
    public event Action<string>? PartialResult;
    public event Action<string>? Result;
    public event Action<int>? Error;
    public void ConfigureCallbacks(Action<AndroidRuntimeEvent> dispatchEvent) => _dispatchEvent = dispatchEvent;

    public string StartStreamingRecognition(AndroidScriptEntity entity, string? requestId = null,
        string? onPartialCallback = "asr_partial", string? onCompletedCallback = "asr_completed",
        string? onErrorCallback = "asr_error", string eventType = "asr")
    {
        Cancel();
        _requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        _entityId = entity.Id;
        _partialCallback = onPartialCallback ?? string.Empty;
        _completedCallback = onCompletedCallback ?? string.Empty;
        _errorCallback = onErrorCallback ?? string.Empty;
        _eventType = eventType;
        _silenceSeconds = null;
        return Start() ? _requestId : string.Empty;
    }

    public string StartWakeWordMonitoring(AndroidScriptEntity entity, IEnumerable<string> wakeWords,
        string? requestId = null, float? chunkDurationSeconds = null, float? extensionDurationSeconds = null,
        float? trailingSilencePaddingSeconds = null, string? onDetectedCallback = "asr_wake_word_detected",
        string? onErrorCallback = "asr_wake_word_error")
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
        // Android's recognizer owns capture chunks. Forward its supported silence
        // hint; the desktop chunk/extension parameters remain source compatible.
        _silenceSeconds = trailingSilencePaddingSeconds;
        return Start() ? _requestId : string.Empty;
    }

    public void StopWakeWordMonitoring() { if (_monitoring) Cancel(); }
    public void StopStreamingRecognition(string requestId) { if (requestId == _requestId) Stop(); }

    public bool Start(string language = "")
    {
        if (!IsAvailable)
        {
            _monitoring = false;
            Dispatch("error", _errorCallback, error: "Android speech recognition is unavailable.", isFinal: true);
            return false;
        }
        _language = language;
        int generation = ++_generation;
        _listening = true;
        _main.Post(() => StartNative(generation));
        return true;
    }

    private void StartNative(int generation)
    {
        if (generation != _generation) return;
        try
        {
            _recognizer?.Destroy();
            _recognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context)
                ?? throw new InvalidOperationException("Could not create Android speech recognizer.");
            _recognizer.SetRecognitionListener(new SessionListener(this, generation));
            Intent intent = new(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
            intent.PutExtra(RecognizerIntent.ExtraLanguage, string.IsNullOrWhiteSpace(_language) ? Java.Util.Locale.Default!.ToString() : _language);
            if (_silenceSeconds.HasValue)
                intent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, (long)(Math.Clamp(_silenceSeconds.Value, 0.1f, 10.0f) * 1000));
            _recognizer.StartListening(intent);
        }
        catch (Exception ex)
        {
            _listening = _monitoring = false;
            Dispatch("error", _errorCallback, error: ex.Message, isFinal: true);
        }
    }

    public void Stop()
    {
        _monitoring = false;
        int generation = _generation;
        _main.Post(() => { if (generation == _generation) _recognizer?.StopListening(); });
    }

    public void Cancel()
    {
        ++_generation;
        _monitoring = _listening = false;
        _main.Post(() => { _recognizer?.Cancel(); _recognizer?.Destroy(); _recognizer = null; });
    }

    public void OnResults(Bundle? results)
    {
        _listening = false;
        LastText = Extract(results);
        if (_monitoring)
        {
            string normalized = Normalize(LastText);
            string? matched = _wakeWords.FirstOrDefault(word => normalized.Contains(Normalize(word), StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                _monitoring = false;
                Dispatch("wake_word_detected", _completedCallback, LastText, isFinal: true, wakeWord: matched);
            }
            else RestartMonitor();
            return;
        }
        Result?.Invoke(LastText);
        Dispatch("completed", _completedCallback, LastText, isFinal: true);
    }

    public void OnPartialResults(Bundle? results)
    {
        string text = Extract(results);
        if (text.Length == 0 || _monitoring) return;
        LastText = text;
        PartialResult?.Invoke(text);
        Dispatch("partial", _partialCallback, text);
    }

    public void OnError(SpeechRecognizerError error)
    {
        _listening = false;
        if (_monitoring && error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout)
        {
            RestartMonitor();
            return;
        }
        _monitoring = false;
        Error?.Invoke((int)error);
        Dispatch("error", _errorCallback, error: error.ToString(), isFinal: true);
    }

    private void RestartMonitor()
    {
        int generation = _generation;
        _main.PostDelayed(() => { if (_monitoring && generation == _generation) Start(_language); }, 300);
    }

    private void Dispatch(string eventName, string callback, string text = "", string error = "", bool isFinal = false, string wakeWord = "")
        => _dispatchEvent?.Invoke(new AndroidRuntimeEvent(_eventType, _requestId, eventName, Vector2.Zero, text, _entityId,
            Error: error, CallbackName: callback, IsFinal: isFinal, WakeWord: wakeWord, RecognizedText: text));

    private static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit));
    private static string Extract(Bundle? bundle) => bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() ?? string.Empty;
    public void OnBeginningOfSpeech() { }
    public void OnBufferReceived(byte[]? buffer) { }
    public void OnEndOfSpeech() { }
    public void OnEvent(int eventType, Bundle? @params) { }
    public void OnReadyForSpeech(Bundle? @params) { }
    public void OnRmsChanged(float rmsdB) { }
    protected override void Dispose(bool disposing) { if (disposing) Cancel(); base.Dispose(disposing); }

    private sealed class SessionListener(AndroidScriptAsr owner, int generation) : Java.Lang.Object, IRecognitionListener
    {
        public void OnResults(Bundle? results) { if (generation == owner._generation) owner.OnResults(results); }
        public void OnPartialResults(Bundle? results) { if (generation == owner._generation) owner.OnPartialResults(results); }
        public void OnError(SpeechRecognizerError error) { if (generation == owner._generation) owner.OnError(error); }
        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle? @params) { }
        public void OnReadyForSpeech(Bundle? @params) { }
        public void OnRmsChanged(float rmsdB) { }
    }
}
