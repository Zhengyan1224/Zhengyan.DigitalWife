// Native boundary doubles: the production ASR adapter runs unchanged on net10.0.
namespace Java.Lang
{
    public class Object : IDisposable
    {
        protected virtual void Dispose(bool disposing) { }
        public void Dispose() => Dispose(true);
    }
}

namespace Java.Util
{
    public sealed class Locale
    {
        public static Locale Default { get; } = new();
        public override string ToString() => "zh_CN";
    }
}

namespace Android.App
{
    public static class Application { public static object Context { get; } = new(); }
}

namespace Android.Content
{
    public sealed class Intent
    {
        public Intent(string action) { }
        public Intent PutExtra(string name, bool value) => this;
        public Intent PutExtra(string name, string value) => this;
        public Intent PutExtra(string name, long value) => this;
    }
}

namespace Android.OS
{
    public sealed class Bundle(string text)
    {
        public IList<string> GetStringArrayList(string key) => [text];
    }

    public sealed class Looper { public static Looper MainLooper { get; } = new(); }

    public sealed class Handler
    {
        public Handler(Looper looper) { }
        private static readonly List<(long Time, Action Action)> Pending = [];
        public static long Now { get; private set; }
        public bool Post(Action action) => PostDelayed(action, 0);
        public bool PostDelayed(Action action, long milliseconds)
        {
            Pending.Add((Now + milliseconds, action));
            return true;
        }
        public static void Advance(long milliseconds = 0)
        {
            long end = Now + milliseconds;
            while (true)
            {
                int index = -1;
                for (int i = 0; i < Pending.Count; i++)
                    if (Pending[i].Time <= end && (index < 0 || Pending[i].Time < Pending[index].Time)) index = i;
                if (index < 0) break;
                var item = Pending[index];
                Pending.RemoveAt(index);
                Now = item.Time;
                item.Action();
            }
            Now = end;
        }
        public static void Reset() { Pending.Clear(); Now = 0; }
    }
}

namespace Android.Speech
{
    public enum SpeechRecognizerError { NetworkTimeout = 1, Network = 2, Audio = 3, Client = 5, SpeechTimeout = 6, NoMatch = 7, RecognizerBusy = 8, InsufficientPermissions = 9 }

    public interface IRecognitionListener
    {
        void OnResults(OS.Bundle? results);
        void OnPartialResults(OS.Bundle? results);
        void OnError(SpeechRecognizerError error);
        void OnBeginningOfSpeech();
        void OnBufferReceived(byte[]? buffer);
        void OnEndOfSpeech();
        void OnEvent(int eventType, OS.Bundle? parameters);
        void OnReadyForSpeech(OS.Bundle? parameters);
        void OnRmsChanged(float rmsdB);
    }

    public static class RecognizerIntent
    {
        public const string ActionRecognizeSpeech = "recognize";
        public const string ExtraLanguageModel = "language_model";
        public const string LanguageModelFreeForm = "free_form";
        public const string ExtraPartialResults = "partial_results";
        public const string ExtraLanguage = "language";
        public const string ExtraSpeechInputCompleteSilenceLengthMillis = "silence_length";
    }

    public sealed class SpeechRecognizer
    {
        public const string ResultsRecognition = "results";
        public static List<SpeechRecognizer> Sessions { get; } = [];
        public static bool Available { get; set; } = true;
        public IRecognitionListener Listener { get; private set; } = null!;
        public int StopCalls { get; private set; }
        public bool Destroyed { get; private set; }
        public static List<(string Operation, SpeechRecognizer Session, long Time)> Operations { get; } = [];
        public bool Canceled { get; private set; }
        public static bool IsRecognitionAvailable(object context) => Available;
        public static SpeechRecognizer CreateSpeechRecognizer(object context)
        {
            SpeechRecognizer session = new();
            Sessions.Add(session);
            return session;
        }
        public void SetRecognitionListener(IRecognitionListener listener) => Listener = listener;
        public void StartListening(Content.Intent intent) => Operations.Add(("start", this, OS.Handler.Now));
        public void StopListening() => StopCalls++;
        public void Cancel() { Canceled = true; Operations.Add(("cancel", this, OS.Handler.Now)); }
        public void Destroy() { Destroyed = true; Operations.Add(("destroy", this, OS.Handler.Now)); }
        public void Ready() => Listener.OnReadyForSpeech(null);
        public void BeginSpeech() => Listener.OnBeginningOfSpeech();
        public void Complete(string text) => Listener.OnResults(new(text));
        public void Partial(string text) => Listener.OnPartialResults(new(text));
        public void Fail(SpeechRecognizerError error) => Listener.OnError(error);
    }
}
