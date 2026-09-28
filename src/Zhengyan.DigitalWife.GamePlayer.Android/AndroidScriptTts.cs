using Android.App;
using Android.OS;
using Android.Speech.Tts;
using Java.Util;
using System.Collections.Concurrent;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptTts : Java.Lang.Object, TextToSpeech.IOnInitListener, IAndroidScriptTts
{
    private static readonly Lazy<AndroidScriptTts> LazyShared = new(() => new());
    private readonly TextToSpeech _engine;
    private readonly Handler _main = new(Looper.MainLooper!);
    private readonly TaskCompletionSource<bool> _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _utterances = new();
    private readonly CompletionListener _listener;
    private bool _ready;

    private AndroidScriptTts()
    {
        _engine = new TextToSpeech(Application.Context, this);
        _listener = new CompletionListener(this);
        _engine.SetOnUtteranceProgressListener(_listener);
    }

    public static AndroidScriptTts Shared => LazyShared.Value;
    public bool IsReady => _ready;
    public void OnInit(OperationResult status)
    {
        _ready = status == OperationResult.Success;
        _initialized.TrySetResult(_ready);
    }

    public bool Speak(string text, string? language = null, bool flush = true)
    {
        if (!_ready || string.IsNullOrWhiteSpace(text)) return false;
        if (!string.IsNullOrWhiteSpace(language)) _engine.SetLanguage(Locale.ForLanguageTag(language));
        if (flush) CancelUtterances();
        return _engine.Speak(text, flush ? QueueMode.Flush : QueueMode.Add, null, Guid.NewGuid().ToString("N")) == OperationResult.Success;
    }

    public async Task SpeakAsync(string text, float speed = 1.0f, float volume = 1.0f, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!await _initialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Android text-to-speech initialization failed.");
        string id = Guid.NewGuid().ToString("N");
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _main.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
            CancelUtterances();
            _utterances[id] = completion;
            try
            {
                _engine.SetSpeechRate(Math.Clamp(speed, 0.1f, 4.0f));
                using Bundle parameters = new();
                parameters.PutFloat(TextToSpeech.Engine.KeyParamVolume, Math.Clamp(volume, 0.0f, 1.0f));
                if (_engine.Speak(text, QueueMode.Flush, parameters, id) != OperationResult.Success)
                    Complete(id, new InvalidOperationException("Android text-to-speech could not start."));
            }
            catch (Exception ex) { Complete(id, ex); }
        });
        try { await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally { _utterances.TryRemove(id, out _); }
    }

    public void Stop()
    {
        CancelUtterances();
        _main.Post(() => { if (_ready) _engine.Stop(); });
    }

    private void CancelUtterances()
    {
        foreach (string id in _utterances.Keys)
            if (_utterances.TryRemove(id, out var completion)) completion.TrySetCanceled();
    }

    private void Complete(string? id, Exception? error = null)
    {
        if (id is null || !_utterances.TryRemove(id, out var completion)) return;
        if (error is null) completion.TrySetResult(true);
        else completion.TrySetException(error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelUtterances();
            _engine.Shutdown();
            _engine.Dispose();
            _listener.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class CompletionListener(AndroidScriptTts owner) : UtteranceProgressListener
    {
        public override void OnStart(string? utteranceId) { }
        public override void OnDone(string? utteranceId) => owner.Complete(utteranceId);
        [Obsolete("Required override for the Android UtteranceProgressListener contract.")]
        public override void OnError(string? utteranceId) => owner.Complete(utteranceId, new InvalidOperationException("Android text-to-speech failed."));
        public override void OnStop(string? utteranceId, bool interrupted)
        {
            if (utteranceId is not null && owner._utterances.TryRemove(utteranceId, out var completion)) completion.TrySetCanceled();
        }
    }
}
