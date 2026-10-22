using System.Numerics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Local capture and Sherpa recognition; never calls a system or remote ASR service.</summary>
public sealed class AndroidScriptAsr : IAndroidScriptAsr, IDisposable
{
    private readonly GameProjectAsrSettings _settings;
    private readonly IAudioSource _microphone;
    private readonly Func<ISpeechRecognizer> _createRecognizer;
    private readonly Func<bool> _microphoneAvailable;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private Task _operation = Task.CompletedTask;
    private ISpeechRecognizer? _recognizer;
    private Request? _current;
    private int _generation;
    private bool _disposed;
    private string _lastText = string.Empty;
    private Action<AndroidRuntimeEvent>? _dispatch;
    private Action<Action>? _post;

    internal AndroidScriptAsr(GameProjectAsrSettings settings, IAudioSource microphone,
        Func<ISpeechRecognizer> createRecognizer, Func<bool> microphoneAvailable, ILogger logger)
    { _settings = settings; _microphone = microphone; _createRecognizer = createRecognizer; _microphoneAvailable = microphoneAvailable; _logger = logger; }

    public bool Enabled => !_disposed && _settings.Enabled;
    public bool IsAvailable => Enabled && string.Equals(_settings.Provider, "sherpa", StringComparison.OrdinalIgnoreCase) && _microphoneAvailable();
    public bool IsListening { get { lock (_sync) return _current is not null; } }
    public bool IsWakeWordMonitoring { get { lock (_sync) return _current?.WakeWords.Length > 0; } }
    public string LastText { get { lock (_sync) return _lastText; } }
    public event Action<string>? PartialResult;
    public event Action<string>? Result;
    public event Action<int>? Error;

    internal void ConfigureCallbacks(Action<AndroidRuntimeEvent> dispatch, Action<Action>? post = null)
    { lock (_sync) { _dispatch = dispatch; _post = post; } }

    internal Task PreloadAsync()
    {
        lock (_sync)
        {
            if (_disposed || !_settings.Enabled || !_settings.PreloadOnSceneLoad) return Task.CompletedTask;
            Task previous = _operation;
            return _operation = Task.Run(async () =>
            {
                await IgnoreCancellation(previous).ConfigureAwait(false);
                if (_disposed) return;
                try
                {
                    ISpeechRecognizer recognizer = GetRecognizer();
                    await using IStreamingSpeechRecognitionSession session = recognizer.CreateStreamingSession();
                    await session.WriteAsync(new(new float[1600], new(16000, 1), TimeSpan.Zero)).ConfigureAwait(false);
                    await session.CompleteAsync().ConfigureAwait(false);
                    _logger.LogInformation("ASR local Sherpa preload complete.");
                }
                catch (Exception ex) { _logger.LogError(ex, "ASR local Sherpa preload failed; the next request will report model configuration errors."); }
            });
        }
    }

    public string StartStreamingRecognition(AndroidScriptEntity entity, string? requestId = null,
        string? onPartialCallback = "asr_partial", string? onCompletedCallback = "asr_completed",
        string? onErrorCallback = "asr_error", string eventType = "asr")
        => StartRequest(entity.Id, requestId, onPartialCallback, onCompletedCallback, onErrorCallback, eventType, [], null);

    public string StartWakeWordMonitoring(AndroidScriptEntity entity, IEnumerable<string> wakeWords,
        string? requestId = null, float? chunkDurationSeconds = null, float? extensionDurationSeconds = null,
        float? trailingSilencePaddingSeconds = null, string? onDetectedCallback = "asr_wake_word_detected",
        string? onErrorCallback = "asr_wake_word_error")
    {
        string[] words = wakeWords.Where(word => !string.IsNullOrWhiteSpace(word) && Normalize(word).Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (words.Length == 0 || words.All(word => Normalize(word).Length == 0))
            throw new ArgumentException("At least one meaningful wake word is required.", nameof(wakeWords));
        return StartRequest(entity.Id, requestId, "", onDetectedCallback, onErrorCallback, "asr", words, null);
    }

    public bool Start(string language = "")
        => StartRequest("", null, "", "", "", "asr", [], language).Length > 0;

    private string StartRequest(string entityId, string? requestId, string? partial, string? completed,
        string? error, string type, string[] words, string? language)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCurrent();
            Request request = new(++_generation, entityId, string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId,
                partial ?? "", completed ?? "", error ?? "", type, words, language);
            _current = request;
            _lastText = "";
            Task previous = _operation;
            _operation = Task.Run(async () =>
            {
                await IgnoreCancellation(previous).ConfigureAwait(false);
                await RunAsync(request).ConfigureAwait(false);
            });
            _logger.LogInformation("ASR local start request={Request}, mode={Mode}, provider=sherpa, generation={Generation}.",
                request.Id, words.Length > 0 ? "wake" : "question", request.Generation);
            return request.Id;
        }
    }

    public void StopStreamingRecognition(string requestId)
    { lock (_sync) { if (_current is { } request && request.WakeWords.Length == 0 && request.Id == requestId) request.CaptureStop.Cancel(); } }
    public void StopWakeWordMonitoring()
    { lock (_sync) { if (_current?.WakeWords.Length > 0) { ++_generation; CancelCurrent(); } } }
    public void Stop()
    { lock (_sync) { if (_current is { } request) { if (request.WakeWords.Length > 0) Cancel(); else request.CaptureStop.Cancel(); } } }
    public void Cancel()
    { lock (_sync) { ++_generation; CancelCurrent(); } }
    private void CancelCurrent() { _current?.Lifetime.Cancel(); _current = null; }

    private async Task RunAsync(Request request)
    {
        try
        {
            request.Lifetime.Token.ThrowIfCancellationRequested();
            if (!_microphoneAvailable()) throw new InvalidOperationException("Android microphone permission is not granted.");
            ISpeechRecognizer recognizer = GetRecognizer();
            AudioCaptureOptions capture = new()
            {
                SampleRate = Math.Max(8000, _settings.Capture.SampleRate), Channels = Math.Clamp(_settings.Capture.Channels, 1, 2),
                FramesPerBuffer = (uint)Math.Clamp(_settings.Capture.FramesPerBuffer, 64, 8192)
            };
            // Switching wake/question modes waits for the previous capture and
            // stream to close. The two requests never share a Sherpa stream.
            while (!request.Lifetime.IsCancellationRequested)
            {
                await using IStreamingSpeechRecognitionSession session = recognizer.CreateStreamingSession(new()
                {
                    Language = string.IsNullOrWhiteSpace(request.Language) ? _settings.Sherpa.Language : request.Language,
                    PartialResultInterval = TimeSpan.FromSeconds(Math.Max(.05, _settings.PartialResultIntervalSeconds))
                });
                string partialText = "";
                string finalText = "";
                string wakeText = "";
                string? detectedWakeWord = null;
                using CancellationTokenSource segmentStop = CancellationTokenSource.CreateLinkedTokenSource(request.Lifetime.Token);
                using CancellationTokenSource updateLifetime = CancellationTokenSource.CreateLinkedTokenSource(request.Lifetime.Token);
                Task updates = Task.Run(async () =>
                {
                    await foreach (SpeechRecognitionUpdate update in session.GetUpdatesAsync(updateLifetime.Token).ConfigureAwait(false))
                    {
                        if (update.IsFinal) finalText = update.Text;
                        else if (!string.IsNullOrWhiteSpace(update.Text))
                        {
                            partialText = update.Text;
                            if (request.WakeWords.Length == 0) Publish(request, "partial", update.Text);
                            else if (request.WakeWords.FirstOrDefault(word => Normalize(update.Text).Contains(Normalize(word), StringComparison.OrdinalIgnoreCase)) is { } matched)
                            {
                                wakeText = update.Text;
                                detectedWakeWord = matched;
                                segmentStop.Cancel(); // Trigger locally without waiting for the character's whole reply to finish.
                            }
                        }
                    }
                });
                bool heardSpeech = false;
                double silence = 0;
                double speechDuration = 0;
                Queue<AudioChunk> preRoll = new();
                double preRollSeconds = 0;
                try
                {
                    if (!request.CaptureStop.IsCancellationRequested)
                    {
                        await foreach (AudioChunk chunk in CaptureBufferedAsync(capture, segmentStop.Token, request.CaptureStop.Token).ConfigureAwait(false))
                        {
                            bool speech = Rms(chunk) >= Math.Clamp(_settings.SpeechRmsThreshold, .0001f, 1f);
                            if (!heardSpeech && !speech)
                            {
                                preRoll.Enqueue(chunk);
                                preRollSeconds += chunk.Duration.TotalSeconds;
                                while (preRollSeconds > .25 && preRoll.Count > 1)
                                    preRollSeconds -= preRoll.Dequeue().Duration.TotalSeconds;
                                continue;
                            }
                            if (!heardSpeech)
                                while (preRoll.TryDequeue(out AudioChunk? buffered))
                                    await session.WriteAsync(buffered, request.Lifetime.Token).ConfigureAwait(false);
                            heardSpeech |= speech;
                            if (heardSpeech)
                            {
                                speechDuration += chunk.Duration.TotalSeconds;
                                silence = speech ? 0 : silence + chunk.Duration.TotalSeconds;
                            }
                            await session.WriteAsync(chunk, request.Lifetime.Token).ConfigureAwait(false);
                            if (heardSpeech && (silence + 1e-6 >= Math.Clamp(_settings.EndpointSilenceSeconds, .1f, 10f) || speechDuration >= 30)) break;
                        }
                    }
                }
                catch (OperationCanceledException) when (segmentStop.IsCancellationRequested) { }
                finally
                {
                    try
                    {
                        if (!request.Lifetime.IsCancellationRequested) await session.CompleteAsync(request.Lifetime.Token).ConfigureAwait(false);
                    }
                    catch { updateLifetime.Cancel(); throw; }
                    finally
                    {
                        if (request.Lifetime.IsCancellationRequested) updateLifetime.Cancel();
                        try { await updates.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { }
                    }
                }
                request.Lifetime.Token.ThrowIfCancellationRequested();
                string text = string.IsNullOrWhiteSpace(finalText) ? partialText : finalText;
                if (request.WakeWords.Length > 0)
                {
                    if (!string.IsNullOrWhiteSpace(wakeText)) text = wakeText;
                    string normalized = Normalize(text);
                    string? matched = detectedWakeWord ?? request.WakeWords.FirstOrDefault(word => normalized.Contains(Normalize(word), StringComparison.OrdinalIgnoreCase));
                    if (matched is null) continue;
                    Finish(request);
                    Publish(request, "wake_word_detected", text, wakeWord: matched);
                    return;
                }
                if (!string.IsNullOrWhiteSpace(text) || request.CaptureStop.IsCancellationRequested)
                {
                    Finish(request);
                    Publish(request, "completed", text);
                    return;
                }
                // Silence or an unrecognized utterance is not an error. Resume
                // capture without resetting the deadline owned by the script.
            }
        }
        catch (OperationCanceledException) when (request.Lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASR local failed request={Request}, callback={Callback}.", request.Id, request.ErrorCallback);
            Finish(request);
            Publish(request, "error", error: ex.Message);
        }
        finally { request.Dispose(); }
    }

    private ISpeechRecognizer GetRecognizer() => _recognizer ??= _createRecognizer();
    private async IAsyncEnumerable<AudioChunk> CaptureBufferedAsync(AudioCaptureOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        CancellationToken captureStop)
    {
        // Keep AudioRecord draining while Sherpa decodes. Native device buffers
        // are much shorter than a slow CPU inference step. Bound the queue to
        // roughly 30 seconds, and report overflow rather than dropping words.
        int frames = (int)Math.Max(64u, options.FramesPerBuffer);
        int capacity = Math.Clamp((int)Math.Ceiling(30.0 * options.SampleRate / frames), 4, 8192);
        Channel<AudioChunk> chunks = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(capacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, captureStop);
        Task pump = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                await foreach (AudioChunk chunk in _microphone.CaptureAsync(options, lifetime.Token).ConfigureAwait(false))
                    if (!chunks.Writer.TryWrite(chunk))
                        throw new InvalidOperationException("Local ASR cannot keep up with microphone audio. Use a smaller Sherpa model or reduce rendering load.");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { failure = ex; }
            finally { chunks.Writer.TryComplete(failure); }
        });
        try
        {
            // Explicit Stop closes the microphone and drains audio already
            // captured. Cancel or a wake hit discards it immediately.
            await foreach (AudioChunk chunk in chunks.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return chunk;
        }
        finally { lifetime.Cancel(); await pump.ConfigureAwait(false); }
    }
    private void Finish(Request request) { lock (_sync) { if (ReferenceEquals(_current, request)) _current = null; } }
    private void Publish(Request request, string eventName, string text = "", string error = "", string wakeWord = "")
    {
        if (eventName != "partial") _logger.LogInformation("ASR local event request={Request}, event={Event}, textLength={Length}.", request.Id, eventName, text.Length);
        void Deliver()
        {
            lock (_sync)
            {
                if (_disposed || request.Generation != _generation) return;
                if (eventName != "error") _lastText = text;
            }
            if (eventName == "partial") PartialResult?.Invoke(text);
            else if (eventName == "completed") Result?.Invoke(text);
            else if (eventName == "error") Error?.Invoke(-1);
            string callback = eventName == "partial" ? request.PartialCallback : eventName == "error" ? request.ErrorCallback : request.CompletedCallback;
            _dispatch?.Invoke(new(request.Type, request.Id, eventName, Vector2.Zero, text, request.EntityId,
                Error: error, CallbackName: callback, IsFinal: eventName != "partial", WakeWord: wakeWord, RecognizedText: text));
        }
        if (_post is { } post) post(Deliver); else Deliver();
    }
    private static float Rms(AudioChunk chunk)
    {
        double sum = 0;
        foreach (float sample in chunk.Samples.Span) sum += sample * sample;
        return chunk.Samples.Length == 0 ? 0 : (float)Math.Sqrt(sum / chunk.Samples.Length);
    }
    private static string Normalize(string value) => string.Concat(value.Normalize().Where(char.IsLetterOrDigit));
    private async Task IgnoreCancellation(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "ASR previous operation failed while draining; proceeding with the current request."); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            ++_generation;
            CancelCurrent();
            Task pending = _operation;
            _operation = Task.Run(async () =>
            {
                await IgnoreCancellation(pending).ConfigureAwait(false);
                (_recognizer as IDisposable)?.Dispose();
                (_microphone as IDisposable)?.Dispose();
            });
        }
    }
    private sealed class Request(int generation, string entityId, string id, string partial, string completed, string error, string type, string[] words, string? language) : IDisposable
    {
        internal int Generation { get; } = generation;
        internal string EntityId { get; } = entityId;
        internal string Id { get; } = id;
        internal string PartialCallback { get; } = partial;
        internal string CompletedCallback { get; } = completed;
        internal string ErrorCallback { get; } = error;
        internal string Type { get; } = type;
        internal string[] WakeWords { get; } = words;
        internal string? Language { get; } = language;
        internal CancellationTokenSource Lifetime { get; } = new();
        internal CancellationTokenSource CaptureStop { get; } = new();
        public void Dispose() { Lifetime.Dispose(); CaptureStop.Dispose(); }
    }
}
