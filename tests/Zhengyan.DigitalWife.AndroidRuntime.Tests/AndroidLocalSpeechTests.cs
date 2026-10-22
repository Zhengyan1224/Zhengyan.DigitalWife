using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Speech;

internal static class AndroidLocalSpeechTests
{
    internal static async Task ValidateProjectModelsAsync(string directory)
    {
        directory = Path.GetFullPath(directory);
        GameProject project = GameProjectStore.Load(directory);
        AndroidLocalSpeechModels factory = new(directory, AppContext.BaseDirectory, NullLoggerFactory.Instance);
        ITextToSpeechSynthesizer tts = factory.CreateSynthesizer(project.Voice);
        ISpeechRecognizer asr = factory.CreateRecognizer(project.Asr);
        try
        {
            const string text = "小雨你好，今天天气怎么样";
            long start = Stopwatch.GetTimestamp();
            AudioData audio = await Task.Run(() => tts.SynthesizeAsync(text, new()
            {
                ModelKind = Enum.Parse<SpeechSynthesisModelKind>(project.Voice.ModelKind, true),
                SpeakerId = project.Voice.DefaultSpeakerId, Speed = project.Voice.DefaultSpeed
            }));
            Check(audio.Samples.Length > 0 && audio.Duration > TimeSpan.Zero, "The actual project TTS model returned no audio.");
            Console.WriteLine($"PASS local Sherpa TTS model: {audio.Duration.TotalSeconds:F2}s audio, {Stopwatch.GetElapsedTime(start).TotalSeconds:F2}s synthesis");
            start = Stopwatch.GetTimestamp();
            SpeechRecognitionResult result = await asr.RecognizeAsync(audio);
            Check(!string.IsNullOrWhiteSpace(result.Text), "The actual project ASR model returned no text for generated speech.");
            Console.WriteLine($"PASS local Sherpa ASR model: {result.Text}; {Stopwatch.GetElapsedTime(start).TotalSeconds:F2}s recognition");
            string artifact = Path.Combine(Path.GetTempPath(), "DigitalWife-local-speech-validation.wav");
            await WaveFile.WriteAsync(artifact, audio);
            Console.WriteLine("Generated audio: " + artifact);

            using Fixture pipeline = new();
            using AndroidScriptAsr local = new(project.Asr, pipeline.Mic, () => factory.CreateRecognizer(project.Asr), () => true, NullLogger.Instance);
            ConcurrentQueue<AndroidRuntimeEvent> events = new();
            local.ConfigureCallbacks(events.Enqueue);
            local.StartWakeWordMonitoring(pipeline.Entity, ["小雨", "晓雨", "小玉", "小宇", "小鱼"]);
            AudioData prepared = audio.ToMono().Resample(16000);
            pipeline.Mic.SendAudio(prepared);
            pipeline.Mic.Send(0, 20);
            await WaitAsync(() => !events.IsEmpty, 60000);
            Check(events.TryPeek(out AndroidRuntimeEvent? wake) && wake.EventName == "wake_word_detected",
                "Local wake monitoring did not recognize the actual synthesized wake phrase: " + string.Join("; ", events.Select(value => value.EventName + ": " + value.Error + " / " + value.Text)));
            Console.WriteLine("PASS actual Sherpa local wake trigger: " + wake!.WakeWord);
            while (events.TryDequeue(out _)) { }
            local.StartStreamingRecognition(pipeline.Entity, "actual-question");
            pipeline.Mic.SendAudio(prepared);
            pipeline.Mic.Send(0, 20);
            await WaitAsync(() => events.Any(value => value.EventName is "completed" or "error"), 60000);
            Check(events.Last().EventName == "completed" && events.Last().Text.Length > 0,
                "Actual local ASR question did not complete on speech endpoint.");
            Console.WriteLine("PASS actual Sherpa local question endpoint: " + events.Last().Text);
        }
        finally { (tts as IDisposable)?.Dispose(); (asr as IDisposable)?.Dispose(); }
    }

    internal static void TestConversation()
    {
        using Fixture f = new();
        for (int turn = 0; turn < 2; turn++)
        {
            f.Start();
            f.Mic.Send(0, 30);
            Wait(() => f.Mic.Reads >= (turn + 1) * 30);
            Check(f.Asr.IsListening && f.Events.Count == turn, "Silence closed the 30-second local conversation window.");
            f.Recognizer.Text = "下一轮问题";
            f.Mic.Send(.1f, 4);
            f.Mic.Send(0, 2);
            Wait(() => f.Events.Count == turn + 1);
            Check(f.Events.Last().EventName == "completed" && f.Events.Last().Text == "下一轮问题" && !f.Asr.IsListening,
                "The next question required another wake word or did not complete after silence.");
        }
        f.Start();
        f.Asr.StopStreamingRecognition("question");
        Wait(() => f.Events.Count == 3);
        Check(f.Events.Last().Text == "", "Explicit idle stop did not complete with empty text.");
    }

    internal static void TestWakeAndSwitch()
    {
        using Fixture f = new();
        f.Recognizer.Text = "无关语句";
        f.Asr.StartWakeWordMonitoring(f.Entity, ["小雨"], "wake");
        f.Mic.Send(.1f, 3); f.Mic.Send(0, 2);
        Wait(() => f.Recognizer.Completions == 1);
        Check(f.Events.Count == 0 && f.Asr.IsWakeWordMonitoring, "Unrelated speech ended wake monitoring.");
        f.Recognizer.Text = "小，雨，请听我说";
        f.Mic.Send(.1f, 3); f.Mic.Send(0, 2);
        Wait(() => f.Events.Count == 1);
        Check(f.Events.Last().WakeWord == "小雨" && !f.Asr.IsWakeWordMonitoring, "Local normalized wake match failed.");
        f.Asr.StartWakeWordMonitoring(f.Entity, ["小雨"], "interrupt");
        Wait(() => f.Mic.Active == 1);
        f.Start();
        f.Recognizer.Text = "继续对话";
        f.Mic.Send(.1f, 3); f.Mic.Send(0, 2);
        Wait(() => f.Events.Count == 2);
        Check(f.Mic.MaxActive == 1 && f.Events.Last().Id == "question", "Wake/question switch overlapped capture or sent the wrong request.");
    }

    internal static void TestCancellationAndFailures()
    {
        using Fixture f = new();
        Queue<Action> callbacks = new();
        f.Asr.ConfigureCallbacks(f.Events.Enqueue, callbacks.Enqueue);
        f.Start();
        f.Recognizer.Text = "old text";
        f.Mic.Send(.1f, 3); f.Mic.Send(0, 2);
        Wait(() => !f.Asr.IsListening);
        f.Start();
        while (callbacks.TryDequeue(out Action? action)) action();
        Check(f.Events.Count == 0, "An old queued completion affected a new request.");
        f.Asr.Cancel();
        Wait(() => f.Mic.Active == 0);
        Check(!f.Asr.IsListening, "Canceled capture remained open.");

        using Fixture unavailable = new(false);
        unavailable.Start();
        Wait(() => unavailable.Events.Count == 1);
        Check(unavailable.Events.Last().Error.Contains("permission") && unavailable.Mic.Reads == 0, "Permission failure silently used another recognizer.");
    }

    internal static void TestTtsPlaybackAndCancel()
    {
        GameProjectVoiceSettings settings = new() { Enabled = true, ModelKind = "Vits", DefaultSpeakerId = 2 };
        FakeTts synth = new();
        FakePlayer player = new();
        using AndroidScriptTts tts = new(settings, () => synth, player, NullLogger.Instance);
        bool started = false;
        Task speech = tts.SpeakWithProgressAsync("你好", 1.25f, .5f, 3, _ => started = true);
        Wait(() => player.Started);
        Check(started && !speech.IsCompleted && synth.Options?.SpeakerId == 3 && synth.Options.Speed == 1.25f,
            "Local TTS ignored speaker/speed or completed before playback.");
        Check(player.Audio!.Samples.All(s => Math.Abs(s - .25f) < .0001f), "Local volume was not applied to PCM audio.");
        player.Done.TrySetResult();
        speech.GetAwaiter().GetResult();
        Check(tts.IsReady, "Local synthesizer never became ready.");
        player.Started = false; player.Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task interrupted = tts.SpeakAsync("打断测试");
        Wait(() => player.Started);
        tts.Stop();
        try { interrupted.GetAwaiter().GetResult(); throw new Exception("Stop failed to cancel native playback."); }
        catch (OperationCanceledException) { }
    }

    internal static void TestModelPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "local-speech-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            AndroidLocalSpeechModels models = new(root, Path.Combine(root, "engine"), NullLoggerFactory.Instance);
            File.WriteAllText(Path.Combine(root, "tokens.txt"), "test");
            Check(models.ResolvePath("project:tokens.txt") == Path.Combine(root, "tokens.txt"), "Project path was resolved against the APK assembly directory.");
            Check(models.ResolvePath("app:voice/model.onnx") == Path.Combine(root, "engine", "voice", "model.onnx"), "Engine asset prefix was lost.");
            try { models.CreateRecognizer(new() { Enabled = true, Provider = "whisper" }); throw new Exception("Whisper was silently enabled."); } catch (NotSupportedException) { }
            try { models.CreateRecognizer(new() { Enabled = true }); throw new Exception("Missing model reached native inference."); } catch (FileNotFoundException) { }
            try { models.ResolvePath("https://example.invalid/model.onnx"); throw new Exception("Local inference accepted a network model URL."); } catch (NotSupportedException) { }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void TestWakePartialAndSlowCapture()
    {
        using Fixture f = new();
        f.Recognizer.EmitPartial = true;
        f.Recognizer.DecodeDelayMilliseconds = 30;
        f.Recognizer.Text = "小雨";
        f.Asr.StartWakeWordMonitoring(f.Entity, ["小雨"]);
        f.Mic.Send(.1f, 3); // No trailing silence: a partial wake must interrupt capture.
        Wait(() => f.Events.Count == 1);
        Check(f.Events.Last().EventName == "wake_word_detected" && f.Mic.Active == 0,
            "Local partial wake detection waited for trailing silence or left capture open.");
        f.Recognizer.EmitPartial = false;
        f.Recognizer.FailWrites = true;
        f.Start();
        f.Mic.Send(.1f, 1);
        Wait(() => f.Events.Count == 2);
        Check(f.Events.Last().EventName == "error" && !f.Asr.IsListening && f.Mic.Active == 0,
            "A decoding error hung the updates reader or failed to release the microphone.");
    }

    internal static void TestStopDrainsCapturedAudio()
    {
        using Fixture f = new();
        f.Recognizer.DecodeDelayMilliseconds = 15;
        f.Recognizer.Text = "已经录下的问题";
        f.Start();
        f.Mic.Send(.1f, 20);
        Wait(() => f.Mic.Reads == 20);
        f.Asr.StopStreamingRecognition("question");
        Wait(() => f.Events.Count == 1);
        Check(f.Events.Last().Text == "已经录下的问题" && f.Recognizer.SpeechChunks == 20 && f.Mic.Active == 0,
            "Stop discarded captured speech while the CPU decoder was catching up.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeScene _scene = new("test.scene", new()
        { Entities = [new() { Id = "body", Name = "Body" }] }, p => p);
        internal AndroidScriptEntity Entity { get; }
        internal FakeMic Mic { get; } = new();
        internal FakeRecognizer Recognizer { get; } = new();
        internal ConcurrentQueue<AndroidRuntimeEvent> Events { get; } = new();
        internal AndroidScriptAsr Asr { get; }
        internal Fixture(bool available = true)
        {
            Entity = new(_scene.Entities.First(), _ => { }, (_, _) => { }, () => null);
            Asr = new(new() { Enabled = true, EndpointSilenceSeconds = .2f }, Mic, () => Recognizer, () => available, NullLogger.Instance);
            Asr.ConfigureCallbacks(Events.Enqueue);
        }
        internal void Start() => Asr.StartStreamingRecognition(Entity, "question", "partial", "complete", "error");
        public void Dispose() { Asr.Dispose(); _scene.Dispose(); }
    }
    private sealed class FakeMic : IAudioSource
    {
        private readonly Channel<AudioChunk> _chunks = Channel.CreateUnbounded<AudioChunk>();
        internal int Reads;
        internal int Active;
        internal int MaxActive;
        internal void Send(float sample, int chunks)
        { for (int i = 0; i < chunks; i++) _chunks.Writer.TryWrite(new(Enumerable.Repeat(sample, 1600).ToArray(), new(16000, 1), TimeSpan.Zero)); }
        internal void SendAudio(AudioData audio)
        {
            for (int offset = 0; offset < audio.Samples.Length; offset += 1600)
                _chunks.Writer.TryWrite(new(audio.Samples.AsMemory(offset, Math.Min(1600, audio.Samples.Length - offset)), audio.Format, TimeSpan.FromSeconds((double)offset / audio.Format.SampleRate)));
        }
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(AudioCaptureOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
        {
            int active = Interlocked.Increment(ref Active); MaxActive = Math.Max(MaxActive, active);
            try { await foreach (AudioChunk chunk in _chunks.Reader.ReadAllAsync(token)) { Interlocked.Increment(ref Reads); yield return chunk; } }
            finally { Interlocked.Decrement(ref Active); }
        }
        public Task<AudioData> RecordAsync(TimeSpan duration, AudioCaptureOptions? options = null, CancellationToken token = default) => throw new NotImplementedException();
        public Task<AudioData> RecordUntilSilenceAsync(VoiceActivityCaptureOptions? options = null, CancellationToken token = default) => throw new NotImplementedException();
    }
    private sealed class FakeRecognizer : ISpeechRecognizer
    {
        internal string Text = "";
        internal int Completions;
        internal bool EmitPartial;
        internal bool FailWrites;
        internal int DecodeDelayMilliseconds;
        internal int SpeechChunks;
        public string Name => "local-test";
        public IStreamingSpeechRecognitionSession CreateStreamingSession(StreamingSpeechRecognitionOptions? options = null) => new Session(this);
        public Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio, SpeechRecognitionOptions? options = null, CancellationToken token = default) => throw new NotImplementedException();
        public Task<SpeechRecognitionResult> RecognizeFileAsync(string path, SpeechRecognitionOptions? options = null, CancellationToken token = default) => throw new NotImplementedException();
        private sealed class Session(FakeRecognizer owner) : IStreamingSpeechRecognitionSession
        {
            private readonly Channel<SpeechRecognitionUpdate> _updates = Channel.CreateUnbounded<SpeechRecognitionUpdate>();
            private bool _speech;
            public async ValueTask WriteAsync(AudioChunk chunk, CancellationToken token = default)
            {
                if (owner.FailWrites) throw new InvalidOperationException("test local decode failure");
                if (owner.DecodeDelayMilliseconds > 0) await Task.Delay(owner.DecodeDelayMilliseconds, token);
                _speech |= chunk.Samples.Span[0] > .01f;
                if (chunk.Samples.Span[0] > .01f) Interlocked.Increment(ref owner.SpeechChunks);
                if (_speech && owner.EmitPartial) _updates.Writer.TryWrite(new() { Text = owner.Text, IsFinal = false, Offset = TimeSpan.Zero });
            }
            public ValueTask CompleteAsync(CancellationToken token = default)
            { _updates.Writer.TryWrite(new() { Text = _speech ? owner.Text : "", IsFinal = true, Offset = TimeSpan.Zero }); _updates.Writer.TryComplete(); Interlocked.Increment(ref owner.Completions); return ValueTask.CompletedTask; }
            public IAsyncEnumerable<SpeechRecognitionUpdate> GetUpdatesAsync(CancellationToken token = default) => _updates.Reader.ReadAllAsync(token);
            public ValueTask DisposeAsync() { _updates.Writer.TryComplete(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class FakeTts : ITextToSpeechSynthesizer
    {
        internal SpeechSynthesisOptions? Options;
        public string Name => "local-test-tts";
        public Task<AudioData> SynthesizeAsync(string text, SpeechSynthesisOptions? options = null, CancellationToken token = default)
        { Options = options; return Task.FromResult(new AudioData(Enumerable.Repeat(.5f, 1600).ToArray(), new(16000, 1))); }
        public Task<string> SynthesizeToFileAsync(string text, string path, SpeechSynthesisOptions? options = null, CancellationToken token = default) => throw new NotImplementedException();
        public IAsyncEnumerable<AudioChunk> SynthesizeStreamingAsync(string text, SpeechSynthesisOptions? options = null, CancellationToken token = default) => throw new NotImplementedException();
    }
    private sealed class FakePlayer : IAudioPlayer
    {
        internal bool Started;
        internal AudioData? Audio;
        internal TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PlayAsync(AudioData audio, CancellationToken token = default) { Audio = audio; Started = true; await Done.Task.WaitAsync(token); }
        public Task PlayAsync(IAsyncEnumerable<AudioChunk> stream, AudioFormat format, CancellationToken token = default) => throw new NotImplementedException();
        public Task PlayFileAsync(string path, CancellationToken token = default) => throw new NotImplementedException();
    }
    private static void Wait(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { if (DateTime.UtcNow >= limit) throw new TimeoutException("Local speech operation did not finish."); Thread.Sleep(5); }
    }
    private static async Task WaitAsync(Func<bool> condition, int milliseconds)
    {
        long start = Stopwatch.GetTimestamp();
        while (!condition()) { if (Stopwatch.GetElapsedTime(start).TotalMilliseconds > milliseconds) throw new TimeoutException("Actual model recognition did not finish."); await Task.Delay(10); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
