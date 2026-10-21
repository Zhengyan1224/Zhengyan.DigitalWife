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
        Handler.Advance(300);
        fixture.Session.Fail(NativeError.SpeechTimeout);
        Handler.Advance(300);
        fixture.Session.Complete("unrelated speech");
        Handler.Advance(300);
        Check(fixture.Events.Count == 0 && fixture.Api.IsWakeWordMonitoring, "Silence or unrelated speech stopped wake monitoring.");
        fixture.Session.Complete("小雨");
        Check(fixture.Events.Single().WakeWord == "小雨" && !fixture.Api.IsWakeWordMonitoring,
            "Wake-word detection changed while fixing conversation capture.");

        foreach (NativeError error in new[] { NativeError.Audio, NativeError.InsufficientPermissions })
        {
            fixture.Events.Clear();
            fixture.Api.StartWakeWordMonitoring(fixture.Entity, ["小雨"], onErrorCallback: "local_wake_error");
            Handler.Advance(300);
            fixture.Session.Fail(error);
            Check(fixture.Events.Single().Error == error.ToString() && !fixture.Api.IsWakeWordMonitoring,
                "A fatal wake recorder/permission failure was hidden as a network retry.");
        }
    }

    internal static void TestReplyToQuestionTransition()
    {
        using Fixture fixture = new();
        Queue<Action> scriptQueue = new();
        fixture.Api.ConfigureCallbacks(fixture.Events.Add, scriptQueue.Enqueue);
        fixture.Api.StartWakeWordMonitoring(fixture.Entity, ["小雨"], "local_asr_interrupt_monitor",
            onErrorCallback: "local_wake_error");
        Handler.Advance(300);
        Native oldMonitor = fixture.Session;
        oldMonitor.Fail(NativeError.Audio);
        Check(scriptQueue.Count == 1, "The old monitor error was not queued for the script thread.");

        fixture.Api.StopWakeWordMonitoring();
        fixture.StartQuestion();
        while (scriptQueue.TryDequeue(out Action? action)) action();
        Check(fixture.Events.Count == 0 && fixture.Api.IsListening,
            "An error queued during the reply closed the next 30-second question window.");
        oldMonitor.Fail(NativeError.Client);
        Check(scriptQueue.Count == 0, "Canceling an old recognizer sent its client error to the new request.");

        for (int retry = 0; retry < 2; retry++)
        {
            fixture.Session.Fail(retry == 0 ? NativeError.RecognizerBusy : NativeError.Client);
            Check(fixture.Api.IsListening && scriptQueue.Count == 0,
                "A transient recognizer handoff error closed the conversation.");
            Handler.Advance(1500);
        }
        fixture.Session.Ready();
        for (int silence = 0; silence < 4; silence++)
        {
            Handler.Advance(4000);
            fixture.Session.Fail(NativeError.SpeechTimeout);
            Handler.Advance(300);
        }
        Check(scriptQueue.Count == 0 && fixture.Api.IsListening,
            "The conversation stopped while waiting for the next question.");
        fixture.Session.Complete("下一轮问题");
        while (scriptQueue.TryDequeue(out Action? action)) action();
        Check(fixture.Events.Single().CallbackName == "local_question_completed",
            "The next question did not reach its callback after recognizer recovery.");

        fixture.Api.StartWakeWordMonitoring(fixture.Entity, ["小雨"], "local_asr_interrupt_monitor");
        Handler.Advance(300);
        oldMonitor = fixture.Session;
        int operations = Native.Operations.Count;
        fixture.Api.StopWakeWordMonitoring();
        fixture.StartQuestion();
        Check(oldMonitor.Canceled && oldMonitor.Destroyed
            && Native.Operations.Skip(operations).Select(item => item.Operation).SequenceEqual(["cancel", "destroy", "start"]),
            "The question recognizer started before releasing the interrupt monitor's microphone.");

        fixture.Api.ConfigureCallbacks(value =>
        {
            Task<bool> stateRead = Task.Run(() => fixture.Api.IsListening);
            Check(stateRead.Wait(TimeSpan.FromSeconds(2)), "ASR held its native state lock while executing an async script callback.");
            fixture.Events.Add(value);
        }, scriptQueue.Enqueue);
        fixture.Session.Complete("another question");
        while (scriptQueue.TryDequeue(out Action? action)) action();
    }

    internal static void TestTransientErrorLimitsAndDiagnostics()
    {
        using Fixture fixture = new();
        global::Android.Util.Log.Entries.Clear();
        fixture.StartQuestion();
        for (int failure = 0; failure < 4; failure++)
        {
            fixture.Session.Fail(NativeError.RecognizerBusy);
            Handler.Advance(1500);
        }
        Check(fixture.Events.Single().Error == "RecognizerBusy" && !fixture.Api.IsListening,
            "A permanently busy recognizer was retried forever or lost its real error.");
        int sessions = Native.Sessions.Count;
        Handler.Advance(3000);
        Check(Native.Sessions.Count == sessions, "A failed request kept restarting.");
        string logs = string.Join("\n", global::Android.Util.Log.Entries.Select(entry => entry.Message));
        Check(logs.Contains("RecognizerBusy") && logs.Contains("code=8")
            && logs.Contains("local_asr_question") && logs.Contains("local_question_error")
            && global::Android.Util.Log.Entries.Any(entry => entry.Level == "error"),
            "Native ASR errors and retry exhaustion were missing from logcat diagnostics.");

        fixture.StartQuestion();
        fixture.Events.Clear();
        fixture.Session.Ready();
        fixture.Session.Fail(NativeError.Client);
        Check(fixture.Events.Single().Error == "Client", "A client failure after recording started was hidden as a handoff error.");

        fixture.StartQuestion();
        fixture.Events.Clear();
        fixture.Session.Fail(NativeError.RecognizerBusy);
        fixture.Api.StopStreamingRecognition("local_asr_question");
        Handler.Advance();
        Check(fixture.Events.Single().EventName == "completed", "An idle deadline during handoff recovery did not complete.");
        sessions = Native.Sessions.Count;
        Handler.Advance(1500);
        Check(Native.Sessions.Count == sessions, "A handoff retry survived the script's stop.");
    }

    internal static void TestWakeMonitorNetworkRecovery()
    {
        using Fixture fixture = new();
        global::Android.Util.Log.Entries.Clear();
        foreach (string request in new[] { "local_asr_interrupt_monitor", "local_asr_wake_monitor" })
        {
            fixture.Api.StartWakeWordMonitoring(fixture.Entity, ["小雨"], request,
                onDetectedCallback: "local_wake_detected", onErrorCallback: "local_wake_error");
            Handler.Advance(300);
            int[] expectedDelays = [500, 1000, 2000, 4000, 5000, 5000];
            for (int failure = 0; failure < 6; failure++)
            {
                Native old = fixture.Session;
                old.Ready(); // ready does not mean the remote service returned text.
                old.Partial("角色正在回复");
                Handler.Advance(5000);
                long failedAt = Handler.Now;
                old.Fail(failure == 3 ? NativeError.Network : NativeError.NetworkTimeout);
                Check(fixture.Api.IsWakeWordMonitoring && fixture.Api.IsListening && fixture.Events.Count == 0,
                    "A background wake service network timeout reset the reply/conversation state.");
                int sessions = Native.Sessions.Count;
                Handler.Advance(499);
                Check(Native.Sessions.Count == sessions, "Network recovery retried without a delay.");
                Handler.Advance(4501);
                Check(Native.Sessions.Count == sessions + 1, "Network recovery stopped or restarted more than once.");
                Check(Native.Operations.Last(item => item.Operation == "start").Time - failedAt == expectedDelays[failure],
                    "Wake network retries did not back off or exceeded the delay cap.");
                old.Complete("小雨");
                Check(fixture.Events.Count == 0, "A failed network session delivered a stale wake word.");
            }
            fixture.Session.Complete("小雨");
            Check(fixture.Events.Single().CallbackName == "local_wake_detected" && !fixture.Api.IsWakeWordMonitoring,
                "Wake detection did not recover after repeated remote timeouts.");
            fixture.Events.Clear();
        }
        string logs = string.Join("\n", global::Android.Util.Log.Entries.Select(entry => entry.Message));
        Check(logs.Contains("NetworkTimeout") && logs.Contains("code=1") && logs.Contains("mode=wake")
            && logs.Contains("delayMs=5000") && !global::Android.Util.Log.Entries.Any(entry => entry.Level == "error"),
            "Recoverable wake network failures were missing from diagnostics or reported as terminal errors.");

        fixture.Api.StartWakeWordMonitoring(fixture.Entity, ["小雨"], "local_asr_interrupt_monitor");
        Handler.Advance(300);
        fixture.Session.Fail(NativeError.NetworkTimeout);
        fixture.StartQuestion();
        Native question = fixture.Session;
        int count = Native.Sessions.Count;
        Handler.Advance(5000);
        Check(Native.Sessions.Count == count && ReferenceEquals(question, fixture.Session),
            "The reply monitor's pending network restart replaced the next question.");
    }

    internal static void TestQuestionNetworkTimeoutDeadline()
    {
        using Fixture fixture = new();
        fixture.StartQuestion();
        long deadline = Handler.Now + 30_000;
        for (int timeout = 0; timeout < 3; timeout++)
        {
            fixture.Session.Ready();
            Handler.Advance(5000);
            fixture.Session.Fail(NativeError.NetworkTimeout);
            Check(fixture.Api.IsListening && fixture.Events.Count == 0,
                "An idle network timeout closed the question's conversation window.");
            Handler.Advance(5000);
        }
        Check(Handler.Now == deadline, "The idle-network scenario did not reach the script's original deadline.");
        fixture.Api.StopStreamingRecognition("local_asr_question");
        Handler.Advance();
        fixture.Session.Fail(NativeError.NetworkTimeout);
        Check(fixture.Events.Single().EventName == "completed" && fixture.Events[0].Text == "" && !fixture.Api.IsListening,
            "Network recovery extended the 30-second deadline or emitted an error at stop.");
        int sessions = Native.Sessions.Count;
        Handler.Advance(5000);
        Check(Native.Sessions.Count == sessions, "Network retry restarted after the idle deadline.");

        fixture.StartQuestion();
        fixture.Events.Clear();
        fixture.Session.Partial("确实说了问题");
        fixture.Events.Clear();
        fixture.Session.Fail(NativeError.NetworkTimeout);
        Check(fixture.Events.Single().Error == "NetworkTimeout" && !fixture.Api.IsListening,
            "A remote transcription failure after a partial question was hidden as an idle timeout.");

        fixture.StartQuestion();
        fixture.Events.Clear();
        fixture.Session.BeginSpeech();
        fixture.Session.Fail(NativeError.NetworkTimeout);
        Check(fixture.Events.Single().Error == "NetworkTimeout", "A speech network failure without partial results was hidden as silence.");

        fixture.StartQuestion();
        fixture.Events.Clear();
        fixture.Session.Fail(NativeError.NetworkTimeout);
        fixture.Api.StopStreamingRecognition("local_asr_question");
        Handler.Advance();
        Check(fixture.Events.Single().EventName == "completed", "Stop during a network backoff never completed.");
        sessions = Native.Sessions.Count;
        Handler.Advance(5000);
        Check(Native.Sessions.Count == sessions, "A network backoff survived explicit stop.");

        fixture.StartQuestion();
        fixture.Events.Clear();
        fixture.Session.Fail(NativeError.NetworkTimeout);
        Handler.Advance(500);
        fixture.Session.Complete("下一轮问题");
        Check(fixture.Events.Single().Text == "下一轮问题", "The next question required another wake after network recovery.");
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
            Native.Operations.Clear();
            Api.ConfigureCallbacks(Events.Add);
            Entity = new(_scene.Entities.First(), _ => { }, (_, _) => { }, () => null);
        }
        internal void StartQuestion()
        {
            Api.StartStreamingRecognition(Entity, "local_asr_question", "local_question_partial", "local_question_completed", "local_question_error");
            Handler.Advance(300);
        }
        public void Dispose() { Api.Cancel(); Handler.Advance(); _scene.Dispose(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
