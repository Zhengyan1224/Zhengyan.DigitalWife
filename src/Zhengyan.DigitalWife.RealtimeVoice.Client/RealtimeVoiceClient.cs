using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Realtime.OpenAI;

namespace Zhengyan.DigitalWife.RealtimeVoice.Client;

public sealed class RealtimeVoiceClient : IAsyncDisposable, IDisposable
{
    private readonly RealtimeVoiceClientSettings _settings;
    private readonly ILogger<RealtimeVoiceClient> _logger;
    private readonly OpenAiRealtimeClient _client;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _ready;
    private bool _disposed;

    public RealtimeVoiceClient(
        RealtimeVoiceClientSettings settings,
        ILogger<RealtimeVoiceClient>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<RealtimeVoiceClient>.Instance;
        _client = new OpenAiRealtimeClient(_settings.ClientOptions, NullLogger<OpenAiRealtimeClient>.Instance);
    }

    public OpenAiRealtimeSession Session => _settings.Session;

    public OpenAiRealtimeSession? CurrentSession => _client.CurrentSession;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ready)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready)
            {
                return;
            }

            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _client.UpdateSessionAsync(_settings.Session, cancellationToken).ConfigureAwait(false);
            _ready = true;
            _logger.LogInformation("RealtimeVoice client connected with model {Model}.", _settings.Session.Model);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task UpdateSessionAsync(OpenAiRealtimeSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _client.UpdateSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpenAiRealtimeTranscriptionResult> TranscribeAsync(
        AudioData audio,
        bool deleteConversationItem = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await _client.TranscribeAsync(audio, deleteConversationItem, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpenAiRealtimeConversationItem> CreateUserTextConversationItemAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await _client.CreateConversationItemAsync(
            new OpenAiRealtimeConversationItem
            {
                Type = "message",
                Status = "completed",
                Role = "user",
                Content =
                [
                    new OpenAiRealtimeContentPart
                    {
                        Type = "input_text",
                        Text = text.Trim()
                    }
                ]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<OpenAiRealtimeResponseUpdate> CreateResponseAsync(
        OpenAiRealtimeResponseRequest? request = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await foreach (OpenAiRealtimeResponseUpdate update in _client.CreateResponseAsync(request, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public async Task DeleteConversationItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _client.DeleteConversationItemAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetConversationAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _client.ResetConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AudioData> SynthesizeTextAsync(
        string text,
        OpenAiAudioSpeechRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await _client.SynthesizeTextAsync(text, request, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectLock.Dispose();
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
