using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Project-local Sherpa synthesis with native PCM playback.</summary>
public sealed class AndroidScriptTts : IAndroidScriptTts, IDisposable
{
    private readonly GameProjectVoiceSettings _settings;
    private readonly Func<ITextToSpeechSynthesizer> _createSynthesizer;
    private readonly IAudioPlayer _speaker;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private Task _operation = Task.CompletedTask;
    private ITextToSpeechSynthesizer? _synthesizer;
    private readonly HashSet<CancellationTokenSource> _requests = [];
    private bool _disposed;
    private volatile bool _ready;
    internal AndroidScriptTts(GameProjectVoiceSettings settings, Func<ITextToSpeechSynthesizer> createSynthesizer, IAudioPlayer speaker, ILogger logger)
    { _settings = settings; _createSynthesizer = createSynthesizer; _speaker = speaker; _logger = logger; }
    public bool IsReady => !_disposed && _ready;

    internal Task PreloadAsync()
    {
        lock (_sync)
        {
            if (_disposed || !_settings.Enabled || !_settings.PreloadOnSceneLoad) return Task.CompletedTask;
            Task previous = _operation;
            return _operation = Task.Run(async () =>
            {
                await IgnoreFailure(previous).ConfigureAwait(false);
                if (_disposed) return;
                try
                {
                    await SynthesizeAsync(string.IsNullOrWhiteSpace(_settings.WarmUpText) ? "你好" : _settings.WarmUpText,
                        _settings.DefaultSpeed, _settings.DefaultSpeakerId, CancellationToken.None).ConfigureAwait(false);
                    _logger.LogInformation("TTS local Sherpa preload complete.");
                }
                catch (Exception ex) { _logger.LogError(ex, "TTS local Sherpa preload failed; the next speech request will report the error."); }
            });
        }
    }

    public bool Speak(string text, string? language = null, bool flush = true)
    {
        if (_disposed || !_settings.Enabled || string.IsNullOrWhiteSpace(text)) return false;
        _ = ObserveAsync(StartSpeech(text, _settings.DefaultSpeed, _settings.DefaultVolume, _settings.DefaultSpeakerId, flush, null, default));
        return true;
    }
    public Task SpeakAsync(string text, float speed = 1, float volume = 1, CancellationToken cancellationToken = default)
        => StartSpeech(text, speed, volume, _settings.DefaultSpeakerId, true, null, cancellationToken);
    internal Task SpeakWithProgressAsync(string text, float speed, float volume, int? speakerId,
        Action<AudioData>? playbackStarted, CancellationToken cancellationToken = default)
        => StartSpeech(text, speed, volume, speakerId ?? _settings.DefaultSpeakerId, true, playbackStarted, cancellationToken);

    private Task StartSpeech(string text, float speed, float volume, int speakerId, bool flush,
        Action<AudioData>? started, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
            if (!_settings.Enabled) return Task.FromException(new InvalidOperationException("Project voice is disabled. Enable Voice.Enabled."));
            if (flush) CancelRequests();
            CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _requests.Add(lifetime);
            Task previous = _operation;
            return _operation = Task.Run(async () =>
            {
                try
                {
                    await IgnoreFailure(previous).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    long start = Stopwatch.GetTimestamp();
                    AudioData audio = await SynthesizeAsync(text, speed, speakerId, lifetime.Token).ConfigureAwait(false);
                    _logger.LogInformation("TTS local synthesized characters={Characters}, durationSeconds={Duration}, elapsedMs={Elapsed}, speaker={Speaker}.",
                        text.Length, audio.Duration.TotalSeconds, Stopwatch.GetElapsedTime(start).TotalMilliseconds, speakerId);
                    lifetime.Token.ThrowIfCancellationRequested();
                    AudioData adjusted = ApplyVolume(audio, volume);
                    started?.Invoke(adjusted);
                    await _speaker.PlayAsync(adjusted, lifetime.Token).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    _logger.LogInformation("TTS local playback complete.");
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "TTS local speech failed."); throw; }
                finally
                {
                    lock (_sync) _requests.Remove(lifetime);
                    lifetime.Dispose();
                }
            });
        }
    }
    private async Task<AudioData> SynthesizeAsync(string text, float speed, int speakerId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _synthesizer ??= _createSynthesizer();
        AudioData audio = await _synthesizer.SynthesizeAsync(text, new()
        {
            ModelKind = Enum.Parse<SpeechSynthesisModelKind>(_settings.ModelKind, true),
            Speed = Math.Clamp(speed, .1f, 5f), SpeakerId = speakerId
        }, token).ConfigureAwait(false);
        _ready = true;
        return audio;
    }
    private static AudioData ApplyVolume(AudioData audio, float volume)
    {
        float gain = Math.Clamp(volume, 0, 4);
        if (Math.Abs(gain - 1) < .0001) return audio;
        return new(audio.Samples.Select(sample => Math.Clamp(sample * gain, -1, 1)).ToArray(), audio.Format);
    }
    private async Task ObserveAsync(Task operation)
    { try { await operation.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (Exception ex) { _logger.LogError(ex, "TTS local asynchronous request failed."); } }
    private static async Task IgnoreFailure(Task task) { try { await task.ConfigureAwait(false); } catch { } }
    public void Stop() { lock (_sync) CancelRequests(); }
    private void CancelRequests() { foreach (CancellationTokenSource request in _requests) request.Cancel(); }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelRequests();
            Task previous = _operation;
            _operation = Task.Run(async () =>
            {
                await IgnoreFailure(previous).ConfigureAwait(false);
                (_synthesizer as IDisposable)?.Dispose();
                (_speaker as IDisposable)?.Dispose();
            });
        }
    }
}
