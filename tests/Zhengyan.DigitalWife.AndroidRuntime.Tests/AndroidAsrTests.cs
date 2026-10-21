using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Handler = Android.OS.Handler;
using Native = Android.Speech.SpeechRecognizer;
using NativeError = Android.Speech.SpeechRecognizerError;

internal static class AndroidAsrTests
{
    internal static void TestConversationWindow()
    {
        using Fixture fixture = new();
        // Body3.csx starts a new 30-second listening window after every reply.
        for (int turn = 0; turn < 2; turn++)
        {
            fixture.StartQuestion();
            for (int silence = 0; silence < 4; silence++)
            {
                Handler.Advance(4000);
                fixture.Session.Fail(silence % 2 == 0 ? NativeError.SpeechTimeout : NativeError.NoMatch);
                Check(fixture.Api.IsListening && fixture.Events.Count == turn,
                    "A native silence timeout closed the script's conversation window.");
                Handler.Advance(300);
            }
            fixture.Session.Complete("");
            Handler.Advance(300);
            Check(fixture.Events.Count == turn && fixture.Api.IsListening,
                "An empty native result forced the user to wake the character again.");
            fixture.Session.Complete($"question {turn}");
            Check(fixture.Events.Count == turn + 1 && fixture.Events[^1].Text == $"question {turn}"
                && fixture.Events[^1].CallbackName == "local_question_completed" && fixture.Events[^1].IsFinal,
                "A question without another wake word did not complete normally.");
            Check(!fixture.Api.IsListening, "A completed question kept recording during the reply.");
        }

        fixture.StartQuestion();
        long deadline = Handler.Now + 30_000;
        for (int silence = 0; silence < 6; silence++)
        {
            Handler.Advance(4000);
            fixture.Session.Fail(NativeError.SpeechTimeout);
            Handler.Advance(300);
        }
        Check(fixture.Events.Count == 2, "The idle window completed before 30 seconds.");
        Handler.Advance(deadline - Handler.Now);
        fixture.Api.StopStreamingRecognition("local_asr_question");
        Handler.Advance();
        fixture.Session.Fail(NativeError.NoMatch);
        Check(fixture.Events.Count == 3 && fixture.Events[^1].EventName == "completed" && fixture.Events[^1].Text == "",
            "The script's 30-second stop did not complete the idle window.");
        int sessions = Native.Sessions.Count;
        Handler.Advance(1000);
        Check(Native.Sessions.Count == sessions && !fixture.Api.IsListening, "ASR restarted after the idle deadline.");
    }

    internal static void TestStopAndCancellation()
    {
        using Fixture fixture = new();
        fixture.StartQuestion();
        fixture.Session.Partial("unfinished question");
        fixture.Session.Fail(NativeError.NoMatch);
        fixture.Events.Clear();
        fixture.Api.StopStreamingRecognition("other_request");
        Check(fixture.Api.IsListening, "Stopping an unrelated request stopped this conversation.");
        fixture.Api.StopStreamingRecognition("local_asr_question");
        Handler.Advance();
        Check(fixture.Events.Count == 1 && fixture.Events[0].Text == "unfinished question",
            "Stopping between native sessions lost the last partial or never completed.");
        Handler.Advance(300);
        Check(Native.Sessions.Count == 1, "A pending restart survived an explicit stop.");

        fixture.StartQuestion();
        fixture.Session.Partial("second question");
        fixture.Events.Clear();
        fixture.Api.Stop();
        Handler.Advance();
        Check(fixture.Session.StopCalls == 1, "An active recognizer did not receive the explicit stop.");
        fixture.Session.Complete("");
        Check(fixture.Events.Single().Text == "second question", "The final empty result lost the partial at stop.");

        fixture.StartQuestion();
        Native old = fixture.Session;
        old.Fail(NativeError.SpeechTimeout);
        fixture.Api.Cancel();
        Handler.Advance();
        fixture.StartQuestion();
        fixture.Events.Clear();
        old.Complete("stale question");
        old.Fail(NativeError.Network);
        int sessions = Native.Sessions.Count;
        Handler.Advance(300);
        Check(fixture.Events.Count == 0 && Native.Sessions.Count == sessions && fixture.Api.IsListening,
            "A canceled session affected the new question or restarted itself.");

        fixture.Api.Cancel();
        Handler.Advance();
        fixture.Events.Clear();
        fixture.Api.StartStreamingRecognition(fixture.Entity, "local_asr_question");
        fixture.Api.StopStreamingRecognition("local_asr_question");
        Handler.Advance();
        Check(fixture.Events.Single().EventName == "completed" && Native.Sessions.Count == sessions,
            "Stopping before native start left a recording running.");
    }

    internal static void TestErrorsAndWakeWords()
    {
        using Fixture fixture = new();
        foreach (NativeError error in new[] { NativeError.Network, NativeError.Audio, NativeError.InsufficientPermissions })
        {
            fixture.StartQuestion();
            fixture.Events.Clear();
            fixture.Session.Fail(error);
            Check(fixture.Events.Single().CallbackName == "local_question_error" && fixture.Events[0].Error == error.ToString()
                && !fixture.Api.IsListening, "A real recognition failure was silently retried.");
        }
        fixture.Events.Clear();
        fixture.Api.StartWakeWordMonitoring(fixture.Entity, ["小雨"], onDetectedCallback: "local_wake_detected");
        Handler.Advance();
        fixture.Session.Fail(NativeError.SpeechTimeout);
        Handler.Advance(300);
        fixture.Session.Complete("unrelated speech");
        Handler.Advance(300);
        Check(fixture.Events.Count == 0 && fixture.Api.IsWakeWordMonitoring, "Silence or unrelated speech stopped wake monitoring.");
        fixture.Session.Complete("小雨");
        Check(fixture.Events.Single().WakeWord == "小雨" && !fixture.Api.IsWakeWordMonitoring,
            "Wake-word detection changed while fixing conversation capture.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly RuntimeScene _scene = new("asr-test.scene.json", new GameProjectScene
        {
            Entities = [new GameEntity { Id = "body", Name = "Body" }]
        }, path => path);
        internal AndroidScriptAsr Api => AndroidScriptAsr.Shared;
        internal AndroidScriptEntity Entity { get; }
        internal List<AndroidRuntimeEvent> Events { get; } = [];
        internal Native Session => Native.Sessions[^1];
        internal Fixture()
        {
            Api.Cancel();
            Handler.Advance();
            Handler.Reset();
            Native.Sessions.Clear();
            Api.ConfigureCallbacks(Events.Add);
            Entity = new(_scene.Entities.First(), _ => { }, (_, _) => { }, () => null);
        }
        internal void StartQuestion()
        {
            Api.StartStreamingRecognition(Entity, "local_asr_question", "local_question_partial", "local_question_completed", "local_question_error");
            Handler.Advance();
        }
        public void Dispose() { Api.Cancel(); Handler.Advance(); _scene.Dispose(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
