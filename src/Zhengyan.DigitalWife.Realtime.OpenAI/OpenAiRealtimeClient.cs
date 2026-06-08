using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Net.WebSockets;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Realtime.OpenAI;

public sealed class OpenAiRealtimeClient : IAsyncDisposable, IDisposable
{
    private readonly OpenAiRealtimeClientOptions _options;
    private readonly ILogger<OpenAiRealtimeClient> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _conversationSync = new();
    private readonly List<string> _conversationItemIds = [];
    private readonly HttpClient _httpClient;

    private ClientWebSocket? _socket;
    private Channel<OpenAiRealtimeServerEvent>? _events;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoop;
    private bool _disposed;

    public OpenAiRealtimeClient(
        OpenAiRealtimeClientOptions options,
        ILogger<OpenAiRealtimeClient> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient();
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        foreach ((string header, string value) in _options.Headers)
        {
            if (!string.IsNullOrWhiteSpace(header) && !string.IsNullOrWhiteSpace(value))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header, value);
            }
        }
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public OpenAiRealtimeSession? CurrentSession { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsConnected)
        {
            return;
        }

        _socket?.Dispose();
        _socket = new ClientWebSocket();
        _options.ApplyTo(_socket.Options);
        _events = Channel.CreateUnbounded<OpenAiRealtimeServerEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        Uri endpoint = _options.BuildEndpoint();
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.ConnectTimeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(_options.ConnectTimeout);
        }

        _logger.LogInformation("Connecting to Realtime endpoint {Endpoint}.", endpoint);
        await _socket.ConnectAsync(endpoint, timeoutCts.Token);

        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_socket, _events.Writer, _receiveLoopCts.Token), CancellationToken.None);

        while (true)
        {
            OpenAiRealtimeServerEvent serverEvent = await ReadNextEventAsync(cancellationToken);
            if (serverEvent.Type is "session.created" or "session.updated")
            {
                CurrentSession = serverEvent.Session;
                _logger.LogInformation("Realtime session established with model {Model}.", CurrentSession?.Model ?? _options.Model ?? "<unspecified>");
                return;
            }

            ThrowIfError(serverEvent);
        }
    }

    public async Task UpdateSessionAsync(OpenAiRealtimeSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureConnected();

        await SendClientEventAsync(new OpenAiRealtimeClientEvent
        {
            Type = "session.update",
            Session = session
        }, cancellationToken);

        while (true)
        {
            OpenAiRealtimeServerEvent serverEvent = await ReadNextEventAsync(cancellationToken);
            if (serverEvent.Type == "session.updated")
            {
                CurrentSession = serverEvent.Session;
                return;
            }

            ThrowIfError(serverEvent);
        }
    }

    public async Task<OpenAiRealtimeTranscriptionResult> TranscribeAsync(
        AudioData audio,
        bool deleteConversationItem = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        EnsureConnected();

        await AppendInputAudioAsync(audio, cancellationToken);
        await SendClientEventAsync(new OpenAiRealtimeClientEvent
        {
            Type = "input_audio_buffer.commit"
        }, cancellationToken);

        string? committedItemId = null;
        OpenAiRealtimeConversationItem? createdItem = null;

        while (true)
        {
            OpenAiRealtimeServerEvent serverEvent = await ReadNextEventAsync(cancellationToken);
            switch (serverEvent.Type)
            {
                case "input_audio_buffer.committed":
                    committedItemId = serverEvent.ItemId;
                    TrackConversationItem(committedItemId);
                    break;

                case "conversation.item.created" when
                    string.Equals(serverEvent.Item?.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && (committedItemId is null || string.Equals(serverEvent.Item?.Id, committedItemId, StringComparison.Ordinal)):
                    createdItem = serverEvent.Item;
                    TrackConversationItem(createdItem?.Id);
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    if (committedItemId is null || string.Equals(serverEvent.ItemId, committedItemId, StringComparison.Ordinal))
                    {
                        string itemId = serverEvent.ItemId ?? committedItemId ?? Guid.NewGuid().ToString("N");
                        string text = (serverEvent.Transcript ?? string.Empty).Trim();
                        if (deleteConversationItem)
                        {
                            await DeleteConversationItemAsync(itemId, cancellationToken);
                        }

                        return new OpenAiRealtimeTranscriptionResult
                        {
                            ItemId = itemId,
                            Text = text,
                            Item = createdItem
                        };
                    }

                    break;

                default:
                    ThrowIfError(serverEvent);
                    break;
            }
        }
    }

    public async IAsyncEnumerable<OpenAiRealtimeResponseUpdate> CreateResponseAsync(
        OpenAiRealtimeResponseRequest? request = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        request ??= new OpenAiRealtimeResponseRequest();

        OpenAiRealtimeAudioFormat outputFormat = request.Audio?.Format
            ?? CurrentSession?.Audio?.Output?.Format
            ?? OpenAiRealtimeAudioFormat.Pcm16();

        await SendClientEventAsync(new OpenAiRealtimeClientEvent
        {
            Type = "response.create",
            Response = request
        }, cancellationToken);

        string? responseId = null;
        string? assistantItemId = null;
        bool audioTranscriptSeen = false;
        StringBuilder transcriptBuilder = new();
        long emittedSamples = 0;

        while (true)
        {
            OpenAiRealtimeServerEvent serverEvent = await ReadNextEventAsync(cancellationToken);
            switch (serverEvent.Type)
            {
                case "response.created":
                    responseId = serverEvent.Response?.Id ?? serverEvent.ResponseId;
                    yield return new OpenAiRealtimeResponseUpdate
                    {
                        ResponseId = responseId,
                        IsStarted = true,
                        Status = serverEvent.Response?.Status
                    };
                    break;

                case "conversation.item.created" when string.Equals(serverEvent.Item?.Role, "assistant", StringComparison.OrdinalIgnoreCase):
                    assistantItemId = serverEvent.Item?.Id ?? serverEvent.ItemId;
                    TrackConversationItem(assistantItemId);
                    break;

                case "response.output_audio_transcript.delta":
                    if (!audioTranscriptSeen && transcriptBuilder.Length > 0)
                    {
                        transcriptBuilder.Clear();
                    }

                    audioTranscriptSeen = true;
                    if (!string.IsNullOrEmpty(serverEvent.Delta))
                    {
                        transcriptBuilder.Append(serverEvent.Delta);
                        yield return CreateTranscriptUpdate(responseId, assistantItemId, serverEvent.Delta, transcriptBuilder.ToString());
                    }

                    break;

                case "response.output_text.delta" when !audioTranscriptSeen:
                    if (!string.IsNullOrEmpty(serverEvent.Delta))
                    {
                        transcriptBuilder.Append(serverEvent.Delta);
                        yield return CreateTranscriptUpdate(responseId, assistantItemId, serverEvent.Delta, transcriptBuilder.ToString());
                    }

                    break;

                case "response.output_audio.delta":
                    if (!string.IsNullOrEmpty(serverEvent.Delta))
                    {
                        AudioData decoded = OpenAiRealtimeProtocol.DecodePcm16(serverEvent.Delta, outputFormat);
                        AudioChunk chunk = new(
                            decoded.Samples,
                            decoded.Format,
                            TimeSpan.FromSeconds(emittedSamples / (double)decoded.Format.SampleRate / decoded.Format.Channels),
                            false);
                        emittedSamples += decoded.Samples.Length;

                        yield return new OpenAiRealtimeResponseUpdate
                        {
                            ResponseId = responseId,
                            AssistantItemId = assistantItemId,
                            AssistantTranscript = transcriptBuilder.ToString(),
                            AudioChunk = chunk
                        };
                    }

                    break;

                case "response.done":
                    string finalText = transcriptBuilder.Length > 0
                        ? transcriptBuilder.ToString().Trim()
                        : OpenAiRealtimeProtocol.ExtractText(serverEvent.Response?.Output.FirstOrDefault());

                    yield return new OpenAiRealtimeResponseUpdate
                    {
                        ResponseId = responseId ?? serverEvent.Response?.Id,
                        AssistantItemId = assistantItemId,
                        AssistantTranscript = finalText,
                        FinalAssistantText = finalText,
                        IsCompleted = true,
                        Status = serverEvent.Response?.Status
                    };
                    yield break;

                default:
                    ThrowIfError(serverEvent);
                    break;
            }
        }
    }

    public async Task DeleteConversationItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        EnsureConnected();

        await SendClientEventAsync(new OpenAiRealtimeClientEvent
        {
            Type = "conversation.item.delete",
            ItemId = itemId
        }, cancellationToken);

        while (true)
        {
            OpenAiRealtimeServerEvent serverEvent = await ReadNextEventAsync(cancellationToken);
            if (serverEvent.Type == "conversation.item.deleted"
                && string.Equals(serverEvent.ItemId, itemId, StringComparison.Ordinal))
            {
                RemoveConversationItem(itemId);
                return;
            }

            ThrowIfError(serverEvent);
        }
    }

    public async Task ResetConversationAsync(CancellationToken cancellationToken = default)
    {
        string[] itemIds;
        lock (_conversationSync)
        {
            itemIds = _conversationItemIds.ToArray();
        }

        foreach (string itemId in itemIds)
        {
            try
            {
                await DeleteConversationItemAsync(itemId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Realtime conversation item {ItemId}.", itemId);
            }
        }
    }

    public async Task<AudioData> SynthesizeTextAsync(
        string text,
        OpenAiAudioSpeechRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ThrowIfDisposed();

        OpenAiAudioSpeechRequest payload = request ?? new OpenAiAudioSpeechRequest();
        payload.Input = text;
        payload.Model ??= CurrentSession?.Model ?? _options.Model;
        payload.Voice ??= CurrentSession?.Audio?.Output?.Voice;

        Uri endpoint = _options.BuildAudioSpeechEndpoint();
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Audio speech request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {TrimBody(body)}",
                null,
                response.StatusCode);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return string.Equals(payload.ResponseFormat, "pcm", StringComparison.OrdinalIgnoreCase)
            ? await ReadPcmAudioAsync(stream, cancellationToken)
            : await WaveFile.ReadAsync(stream, cancellationToken);
    }

    public async Task<OpenAiRealtimeConversationItem> CreateConversationItemAsync(
        OpenAiRealtimeConversationItem item,
        string? previousItemId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureConnected();

        await SendClientEventAsync(new OpenAiRealtimeClientEvent
        {
            Type = "conversation.item.create",
            Item = item,
            PreviousItemId = previousItemId
        }, cancellationToken);

        while (true)
        {
            OpenAiRealtimeServerEvent serverEvent = await ReadNextEventAsync(cancellationToken);
            if (serverEvent.Type == "conversation.item.created")
            {
                OpenAiRealtimeConversationItem created = serverEvent.Item ?? item;
                TrackConversationItem(created.Id ?? serverEvent.ItemId);
                return created;
            }

            ThrowIfError(serverEvent);
        }
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
        _receiveLoopCts?.Cancel();

        if (_socket is not null)
        {
            if (_socket.State == WebSocketState.Open)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
                }
                catch
                {
                }
            }

            _socket.Dispose();
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch
            {
            }
        }

        _httpClient.Dispose();
        _sendLock.Dispose();
        _receiveLoopCts?.Dispose();
    }

    private async Task AppendInputAudioAsync(AudioData audio, CancellationToken cancellationToken)
    {
        OpenAiRealtimeAudioFormat format = CurrentSession?.Audio?.Input?.Format ?? OpenAiRealtimeAudioFormat.Pcm16();
        AudioData prepared = OpenAiRealtimeProtocol.PrepareAudio(audio, format);

        await foreach (AudioChunk chunk in prepared.ToChunks(_options.OutboundAudioChunkSamples, cancellationToken))
        {
            AudioData chunkAudio = new(chunk.Samples.ToArray(), chunk.Format);
            string encoded = OpenAiRealtimeProtocol.EncodePcm16(chunkAudio, format);
            await SendClientEventAsync(new OpenAiRealtimeClientEvent
            {
                Type = "input_audio_buffer.append",
                Audio = encoded
            }, cancellationToken);
        }
    }

    private async Task SendClientEventAsync(OpenAiRealtimeClientEvent clientEvent, CancellationToken cancellationToken)
    {
        EnsureConnected();
        string payload = OpenAiRealtimeProtocol.SerializeClientEvent(clientEvent);
        byte[] utf8 = Encoding.UTF8.GetBytes(payload);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket!.SendAsync(utf8, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<OpenAiRealtimeServerEvent> ReadNextEventAsync(CancellationToken cancellationToken)
    {
        if (_events is null)
        {
            throw new InvalidOperationException("Realtime event channel is not initialized.");
        }

        try
        {
            return await _events.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException ex)
        {
            throw new InvalidOperationException("Realtime connection was closed.", ex);
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        ChannelWriter<OpenAiRealtimeServerEvent> writer,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream message = new();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    writer.TryComplete();
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                string json = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);

                OpenAiRealtimeServerEvent serverEvent = OpenAiRealtimeProtocol.DeserializeServerEvent(json);
                if (serverEvent.Type == "conversation.item.created")
                {
                    TrackConversationItem(serverEvent.Item?.Id ?? serverEvent.ItemId);
                }
                else if (serverEvent.Type == "conversation.item.deleted")
                {
                    RemoveConversationItem(serverEvent.ItemId);
                }

                await writer.WriteAsync(serverEvent, cancellationToken);
            }

            writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static OpenAiRealtimeResponseUpdate CreateTranscriptUpdate(
        string? responseId,
        string? assistantItemId,
        string delta,
        string accumulated)
    {
        return new OpenAiRealtimeResponseUpdate
        {
            ResponseId = responseId,
            AssistantItemId = assistantItemId,
            TranscriptDelta = delta,
            AssistantTranscript = accumulated
        };
    }

    private static void ThrowIfError(OpenAiRealtimeServerEvent serverEvent)
    {
        ArgumentNullException.ThrowIfNull(serverEvent);

        if (serverEvent.Type != "error")
        {
            return;
        }

        string message = serverEvent.Error?.Message
            ?? $"Realtime request failed with event {serverEvent.EventId ?? "<unknown>"}";
        string? code = serverEvent.Error?.Code;
        string? errorType = serverEvent.Error?.Type;
        string? eventId = serverEvent.Error?.EventId ?? serverEvent.EventId;

        if (!string.IsNullOrWhiteSpace(errorType) || !string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(eventId))
        {
            message = $"Realtime error"
                + (string.IsNullOrWhiteSpace(errorType) ? string.Empty : $" type={errorType}")
                + (string.IsNullOrWhiteSpace(code) ? string.Empty : $" code={code}")
                + (string.IsNullOrWhiteSpace(eventId) ? string.Empty : $" event_id={eventId}")
                + $": {message}";
        }

        throw new InvalidOperationException(message);
    }

    private void TrackConversationItem(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        lock (_conversationSync)
        {
            if (_conversationItemIds.Contains(itemId, StringComparer.Ordinal))
            {
                return;
            }

            _conversationItemIds.Add(itemId);
        }
    }

    private void RemoveConversationItem(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        lock (_conversationSync)
        {
            _conversationItemIds.RemoveAll(existing => string.Equals(existing, itemId, StringComparison.Ordinal));
        }
    }

    private void EnsureConnected()
    {
        ThrowIfDisposed();
        if (!IsConnected)
        {
            throw new InvalidOperationException("Realtime client is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static async Task<AudioData> ReadPcmAudioAsync(Stream stream, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, cancellationToken);
        byte[] raw = buffer.ToArray();
        int sampleCount = raw.Length / sizeof(short);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short value = BitConverter.ToInt16(raw, i * sizeof(short));
            samples[i] = value / (float)short.MaxValue;
        }

        return new AudioData(samples, new AudioFormat(24_000, 1, AudioEncoding.Float32));
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "<empty>";
        }

        string normalized = body.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 800 ? normalized : normalized[..800];
    }
}
