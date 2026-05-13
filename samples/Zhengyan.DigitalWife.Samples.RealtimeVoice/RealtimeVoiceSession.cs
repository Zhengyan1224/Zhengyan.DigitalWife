using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.Speech;

namespace Zhengyan.DigitalWife.Samples.RealtimeVoice;

internal sealed class RealtimeVoiceSession
{
    private readonly WebSocket _socket;
    private readonly RealtimeVoiceBackend _backend;
    private readonly ResolvedRealtimeVoiceOptions _options;
    private readonly ILogger<RealtimeVoiceSession> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _conversationSync = new();
    private readonly List<OpenAiRealtimeConversationItem> _conversationItems = [];
    private readonly MemoryStream _inputAudioBuffer = new();

    private OpenAiRealtimeSession _session;
    private CancellationTokenSource? _responseCts;
    private Task? _responseTask;

    public RealtimeVoiceSession(
        WebSocket socket,
        RealtimeVoiceBackend backend,
        ResolvedRealtimeVoiceOptions options,
        OpenAiRealtimeSession session,
        ILogger<RealtimeVoiceSession> logger)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "session.created",
            EventId = CreateEventId(),
            Session = RealtimeVoiceOptionsResolver.CloneSession(_session)
        }, cancellationToken);

        while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            string? message = await ReceiveTextMessageAsync(cancellationToken);
            if (message is null)
            {
                break;
            }

            OpenAiRealtimeClientEvent clientEvent;
            try
            {
                clientEvent = OpenAiRealtimeProtocol.DeserializeClientEvent(message);
            }
            catch (Exception ex)
            {
                await SendErrorAsync("invalid_request_error", ex.Message, null, cancellationToken);
                continue;
            }

            await HandleClientEventAsync(clientEvent, cancellationToken);
        }

        CancelCurrentResponse();
        if (_responseTask is not null)
        {
            try
            {
                await _responseTask;
            }
            catch
            {
            }
        }
    }

    private async Task HandleClientEventAsync(OpenAiRealtimeClientEvent clientEvent, CancellationToken cancellationToken)
    {
        switch (clientEvent.Type)
        {
            case "session.update":
                await HandleSessionUpdateAsync(clientEvent, cancellationToken);
                break;

            case "input_audio_buffer.append":
                await HandleInputAudioAppendAsync(clientEvent, cancellationToken);
                break;

            case "input_audio_buffer.clear":
                _inputAudioBuffer.SetLength(0);
                await SendServerEventAsync(new OpenAiRealtimeServerEvent
                {
                    Type = "input_audio_buffer.cleared",
                    EventId = CreateEventId()
                }, cancellationToken);
                break;

            case "input_audio_buffer.commit":
                await HandleInputAudioCommitAsync(cancellationToken);
                break;

            case "conversation.item.create":
                await HandleConversationItemCreateAsync(clientEvent, cancellationToken);
                break;

            case "conversation.item.delete":
                await HandleConversationItemDeleteAsync(clientEvent, cancellationToken);
                break;

            case "response.create":
                StartResponseGeneration(clientEvent.Response, cancellationToken);
                break;

            case "response.cancel":
                CancelCurrentResponse();
                break;

            default:
                await SendErrorAsync("invalid_request_error", $"Unsupported client event type '{clientEvent.Type}'.", clientEvent.EventId, cancellationToken);
                break;
        }
    }

    private async Task HandleSessionUpdateAsync(OpenAiRealtimeClientEvent clientEvent, CancellationToken cancellationToken)
    {
        if (clientEvent.Session is null)
        {
            await SendErrorAsync("invalid_request_error", "session.update requires a session payload.", clientEvent.EventId, cancellationToken);
            return;
        }

        OpenAiRealtimeSession update = RealtimeVoiceOptionsResolver.CloneSession(clientEvent.Session);
        if (string.IsNullOrWhiteSpace(update.Model))
        {
            update.Model = _session.Model;
        }

        if (string.IsNullOrWhiteSpace(update.Instructions))
        {
            update.Instructions = _session.Instructions;
        }

        _session = update;

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "session.updated",
            EventId = CreateEventId(),
            Session = RealtimeVoiceOptionsResolver.CloneSession(_session)
        }, cancellationToken);
    }

    private async Task HandleInputAudioAppendAsync(OpenAiRealtimeClientEvent clientEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientEvent.Audio))
        {
            await SendErrorAsync("invalid_request_error", "input_audio_buffer.append requires audio.", clientEvent.EventId, cancellationToken);
            return;
        }

        byte[] raw = Convert.FromBase64String(clientEvent.Audio);
        await _inputAudioBuffer.WriteAsync(raw, cancellationToken);
    }

    private async Task HandleInputAudioCommitAsync(CancellationToken cancellationToken)
    {
        if (_inputAudioBuffer.Length == 0)
        {
            await SendErrorAsync("invalid_request_error", "input_audio_buffer.commit was called with an empty buffer.", null, cancellationToken);
            return;
        }

        byte[] rawAudio = _inputAudioBuffer.ToArray();
        _inputAudioBuffer.SetLength(0);

        string itemId = CreateItemId();
        string? previousItemId = GetLastConversationItemId();
        OpenAiRealtimeConversationItem item = new()
        {
            Id = itemId,
            Type = "message",
            Status = "completed",
            Role = "user",
            Content =
            [
                new OpenAiRealtimeContentPart
                {
                    Type = "input_audio"
                }
            ]
        };

        AddConversationItem(item);

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "input_audio_buffer.committed",
            EventId = CreateEventId(),
            ItemId = itemId,
            PreviousItemId = previousItemId
        }, cancellationToken);

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "conversation.item.created",
            EventId = CreateEventId(),
            Item = item,
            ItemId = itemId,
            PreviousItemId = previousItemId
        }, cancellationToken);

        OpenAiRealtimeInputAudioTranscription? transcription = _session.Audio?.Input?.Transcription;
        if (transcription is null)
        {
            return;
        }

        try
        {
            AudioData audio = DecodeInputAudio(rawAudio);
            SpeechRecognitionResult result = await _backend.RecognizeAsync(audio, transcription, cancellationToken);
            item.Content[0].Transcript = result.Text.Trim();

            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "conversation.item.input_audio_transcription.completed",
                EventId = CreateEventId(),
                ItemId = itemId,
                ContentIndex = 0,
                Transcript = item.Content[0].Transcript
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe committed input audio.");
            await SendErrorAsync("server_error", $"Input transcription failed: {ex.Message}", null, cancellationToken);
        }
    }

    private async Task HandleConversationItemCreateAsync(OpenAiRealtimeClientEvent clientEvent, CancellationToken cancellationToken)
    {
        if (clientEvent.Item is null)
        {
            await SendErrorAsync("invalid_request_error", "conversation.item.create requires an item payload.", clientEvent.EventId, cancellationToken);
            return;
        }

        OpenAiRealtimeConversationItem item = clientEvent.Item;
        item.Id ??= CreateItemId();
        item.Status ??= "completed";
        item.Content = item.Content.Count > 0
            ? item.Content
            : [new OpenAiRealtimeContentPart { Type = "input_text", Text = string.Empty }];

        string? previousItemId = string.IsNullOrWhiteSpace(clientEvent.PreviousItemId)
            ? GetLastConversationItemId()
            : clientEvent.PreviousItemId;

        AddConversationItem(item, previousItemId);

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "conversation.item.created",
            EventId = CreateEventId(),
            Item = item,
            ItemId = item.Id,
            PreviousItemId = previousItemId
        }, cancellationToken);
    }

    private async Task HandleConversationItemDeleteAsync(OpenAiRealtimeClientEvent clientEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientEvent.ItemId))
        {
            await SendErrorAsync("invalid_request_error", "conversation.item.delete requires item_id.", clientEvent.EventId, cancellationToken);
            return;
        }

        bool removed = RemoveConversationItem(clientEvent.ItemId);
        if (!removed)
        {
            await SendErrorAsync("invalid_request_error", $"Conversation item '{clientEvent.ItemId}' was not found.", clientEvent.EventId, cancellationToken);
            return;
        }

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "conversation.item.deleted",
            EventId = CreateEventId(),
            ItemId = clientEvent.ItemId
        }, cancellationToken);
    }

    private void StartResponseGeneration(OpenAiRealtimeResponseRequest? request, CancellationToken cancellationToken)
    {
        CancelCurrentResponse();
        _responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken responseToken = _responseCts.Token;
        _responseTask = Task.Run(() => GenerateResponseAsync(request ?? new OpenAiRealtimeResponseRequest(), responseToken), CancellationToken.None);
    }

    private async Task GenerateResponseAsync(OpenAiRealtimeResponseRequest request, CancellationToken cancellationToken)
    {
        string responseId = $"resp_{Guid.NewGuid():N}";
        string assistantItemId = CreateItemId();
        bool persistConversation = !string.Equals(request.Conversation, "none", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<string> outputModalities = request.OutputModalities is { Count: > 0 }
            ? request.OutputModalities
            : _session.OutputModalities is { Count: > 0 }
                ? _session.OutputModalities
                : ["audio"];
        bool outputAudio = outputModalities.Contains("audio", StringComparer.OrdinalIgnoreCase);
        bool outputText = outputModalities.Contains("text", StringComparer.OrdinalIgnoreCase) && !outputAudio;
        OpenAiRealtimeAudioFormat outputFormat = request.Audio?.Format ?? _session.Audio?.Output?.Format ?? OpenAiRealtimeAudioFormat.Pcm16();
        string? voice = request.Audio?.Voice ?? _session.Audio?.Output?.Voice;

        OpenAiRealtimeConversationItem assistantItem = new()
        {
            Id = assistantItemId,
            Type = "message",
            Status = "in_progress",
            Role = "assistant",
            Content =
            [
                new OpenAiRealtimeContentPart
                {
                    Type = outputAudio ? "audio" : "text",
                    Transcript = outputAudio ? string.Empty : null,
                    Text = outputText ? string.Empty : null
                }
            ]
        };

        if (persistConversation)
        {
            AddConversationItem(assistantItem);
        }

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "response.created",
            EventId = CreateEventId(),
            Response = new OpenAiRealtimeResponseInfo
            {
                Id = responseId,
                Status = "in_progress",
                Output = []
            }
        }, cancellationToken);

        if (persistConversation)
        {
            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "conversation.item.created",
                EventId = CreateEventId(),
                Item = assistantItem,
                ItemId = assistantItemId,
                PreviousItemId = GetPreviousItemIdFor(assistantItemId)
            }, cancellationToken);
        }

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "response.output_item.added",
            EventId = CreateEventId(),
            ResponseId = responseId,
            OutputIndex = 0,
            Item = assistantItem
        }, cancellationToken);

        await SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "response.content_part.added",
            EventId = CreateEventId(),
            ResponseId = responseId,
            ItemId = assistantItemId,
            OutputIndex = 0,
            ContentIndex = 0,
            Part = assistantItem.Content[0]
        }, cancellationToken);

        try
        {
            List<LlmChatMessage> messages = BuildPromptMessages(
                request.Instructions ?? _session.Instructions,
                assistantItemId,
                includeConversationHistory: persistConversation);
            if (messages.Count == 0)
            {
                messages.Add(new LlmChatMessage("user", "请简单打个招呼。"));
            }

            StringBuilder rawAssistant = new();
            StringBuilder emittedTranscript = new();
            Channel<string> deltaChannel = Channel.CreateUnbounded<string>();

            Task producer = Task.Run(async () =>
            {
                try
                {
                    await foreach (LlmStreamUpdate update in _backend.StreamChatAsync(messages, cancellationToken))
                    {
                        if (string.IsNullOrEmpty(update.Delta))
                        {
                            continue;
                        }

                        rawAssistant.Append(update.Delta);
                        if (outputText)
                        {
                            assistantItem.Content[0].Text += update.Delta;
                            await SendServerEventAsync(new OpenAiRealtimeServerEvent
                            {
                                Type = "response.output_text.delta",
                                EventId = CreateEventId(),
                                ResponseId = responseId,
                                ItemId = assistantItemId,
                                OutputIndex = 0,
                                ContentIndex = 0,
                                Delta = update.Delta
                            }, cancellationToken);
                        }
                        else
                        {
                            await deltaChannel.Writer.WriteAsync(update.Delta, cancellationToken);
                        }
                    }

                    deltaChannel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    deltaChannel.Writer.TryComplete(ex);
                    throw;
                }
            }, cancellationToken);

            Task consumer = outputAudio
                ? Task.Run(async () =>
                {
                    async IAsyncEnumerable<string> EnumerateDeltas([EnumeratorCancellation] CancellationToken token)
                    {
                        await foreach (string delta in deltaChannel.Reader.ReadAllAsync(token))
                        {
                            yield return delta;
                        }
                    }

                    await foreach (string sentence in _backend.SentenceChunker.ChunkAsync(EnumerateDeltas(cancellationToken), cancellationToken: cancellationToken))
                    {
                        emittedTranscript.Append(sentence);
                        assistantItem.Content[0].Transcript += sentence;

                        await SendServerEventAsync(new OpenAiRealtimeServerEvent
                        {
                            Type = "response.output_audio_transcript.delta",
                            EventId = CreateEventId(),
                            ResponseId = responseId,
                            ItemId = assistantItemId,
                            OutputIndex = 0,
                            ContentIndex = 0,
                            Delta = sentence
                        }, cancellationToken);

                        AudioData synthesized = await _backend.SynthesizeAsync(sentence, voice, cancellationToken);
                        AudioData prepared = OpenAiRealtimeProtocol.PrepareAudio(synthesized, outputFormat);

                        await foreach (AudioChunk chunk in prepared.ToChunks(4_096, cancellationToken))
                        {
                            AudioData chunkAudio = new(chunk.Samples.ToArray(), chunk.Format);
                            string encoded = OpenAiRealtimeProtocol.EncodePcm16(chunkAudio, outputFormat);
                            await SendServerEventAsync(new OpenAiRealtimeServerEvent
                            {
                                Type = "response.output_audio.delta",
                                EventId = CreateEventId(),
                                ResponseId = responseId,
                                ItemId = assistantItemId,
                                OutputIndex = 0,
                                ContentIndex = 0,
                                Delta = encoded
                            }, cancellationToken);
                        }
                    }
                }, cancellationToken)
                : Task.CompletedTask;

            await Task.WhenAll(producer, consumer);

            if (outputAudio)
            {
                await SendServerEventAsync(new OpenAiRealtimeServerEvent
                {
                    Type = "response.output_audio.done",
                    EventId = CreateEventId(),
                    ResponseId = responseId,
                    ItemId = assistantItemId,
                    OutputIndex = 0,
                    ContentIndex = 0
                }, cancellationToken);

                await SendServerEventAsync(new OpenAiRealtimeServerEvent
                {
                    Type = "response.output_audio_transcript.done",
                    EventId = CreateEventId(),
                    ResponseId = responseId,
                    ItemId = assistantItemId,
                    OutputIndex = 0,
                    ContentIndex = 0,
                    Transcript = assistantItem.Content[0].Transcript
                }, cancellationToken);
            }
            else if (outputText)
            {
                await SendServerEventAsync(new OpenAiRealtimeServerEvent
                {
                    Type = "response.output_text.done",
                    EventId = CreateEventId(),
                    ResponseId = responseId,
                    ItemId = assistantItemId,
                    OutputIndex = 0,
                    ContentIndex = 0,
                    Delta = assistantItem.Content[0].Text
                }, cancellationToken);
            }

            assistantItem.Status = "completed";

            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "response.content_part.done",
                EventId = CreateEventId(),
                ResponseId = responseId,
                ItemId = assistantItemId,
                OutputIndex = 0,
                ContentIndex = 0,
                Part = assistantItem.Content[0]
            }, cancellationToken);

            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "response.output_item.done",
                EventId = CreateEventId(),
                ResponseId = responseId,
                OutputIndex = 0,
                Item = assistantItem
            }, cancellationToken);

            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "response.done",
                EventId = CreateEventId(),
                Response = new OpenAiRealtimeResponseInfo
                {
                    Id = responseId,
                    Status = "completed",
                    Output = [assistantItem]
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            assistantItem.Status = "cancelled";
            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "response.done",
                EventId = CreateEventId(),
                Response = new OpenAiRealtimeResponseInfo
                {
                    Id = responseId,
                    Status = "cancelled",
                    Output = [assistantItem]
                }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            assistantItem.Status = "failed";
            _logger.LogError(ex, "Realtime response generation failed.");
            await SendErrorAsync("server_error", ex.Message, null, CancellationToken.None);
            await SendServerEventAsync(new OpenAiRealtimeServerEvent
            {
                Type = "response.done",
                EventId = CreateEventId(),
                Response = new OpenAiRealtimeResponseInfo
                {
                    Id = responseId,
                    Status = "failed",
                    Output = [assistantItem]
                }
            }, CancellationToken.None);
        }
    }

    private List<LlmChatMessage> BuildPromptMessages(
        string? instructions,
        string currentAssistantItemId,
        bool includeConversationHistory)
    {
        List<LlmChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            messages.Add(new LlmChatMessage("system", instructions.Trim()));
        }

        if (!includeConversationHistory)
        {
            return messages;
        }

        OpenAiRealtimeConversationItem[] snapshot;
        lock (_conversationSync)
        {
            snapshot = _conversationItems.ToArray();
        }

        IEnumerable<OpenAiRealtimeConversationItem> selected = snapshot
            .Where(item => !string.Equals(item.Id, currentAssistantItemId, StringComparison.Ordinal))
            .Where(item => string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.Role))
            .TakeLast(_options.HistoryMaxMessages);

        foreach (OpenAiRealtimeConversationItem item in selected)
        {
            string text = OpenAiRealtimeProtocol.ExtractText(item);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            messages.Add(new LlmChatMessage(item.Role!, text));
        }

        return messages;
    }

    private AudioData DecodeInputAudio(byte[] rawAudio)
    {
        string encoded = Convert.ToBase64String(rawAudio);
        OpenAiRealtimeAudioFormat inputFormat = _session.Audio?.Input?.Format ?? OpenAiRealtimeAudioFormat.Pcm16();
        return OpenAiRealtimeProtocol.DecodePcm16(encoded, inputFormat);
    }

    private void AddConversationItem(OpenAiRealtimeConversationItem item, string? previousItemId = null)
    {
        lock (_conversationSync)
        {
            if (string.IsNullOrWhiteSpace(previousItemId))
            {
                _conversationItems.Add(item);
                return;
            }

            int index = _conversationItems.FindIndex(existing => string.Equals(existing.Id, previousItemId, StringComparison.Ordinal));
            if (index < 0 || index + 1 >= _conversationItems.Count)
            {
                _conversationItems.Add(item);
                return;
            }

            _conversationItems.Insert(index + 1, item);
        }
    }

    private bool RemoveConversationItem(string itemId)
    {
        lock (_conversationSync)
        {
            int index = _conversationItems.FindIndex(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            _conversationItems.RemoveAt(index);
            return true;
        }
    }

    private string? GetLastConversationItemId()
    {
        lock (_conversationSync)
        {
            return _conversationItems.LastOrDefault()?.Id;
        }
    }

    private string? GetPreviousItemIdFor(string itemId)
    {
        lock (_conversationSync)
        {
            int index = _conversationItems.FindIndex(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));
            if (index <= 0)
            {
                return null;
            }

            return _conversationItems[index - 1].Id;
        }
    }

    private void CancelCurrentResponse()
    {
        if (_responseCts is null)
        {
            return;
        }

        try
        {
            _responseCts.Cancel();
        }
        catch
        {
        }
    }

    private async Task<string?> ReceiveTextMessageAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream message = new();

        while (true)
        {
            WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    private async Task SendServerEventAsync(OpenAiRealtimeServerEvent serverEvent, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        string payload = OpenAiRealtimeProtocol.SerializeServerEvent(serverEvent);
        byte[] utf8 = Encoding.UTF8.GetBytes(payload);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(utf8, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private Task SendErrorAsync(string code, string message, string? eventId, CancellationToken cancellationToken)
    {
        return SendServerEventAsync(new OpenAiRealtimeServerEvent
        {
            Type = "error",
            EventId = CreateEventId(),
            Error = new OpenAiRealtimeError
            {
                Type = "error",
                Code = code,
                Message = message,
                EventId = eventId
            }
        }, cancellationToken);
    }

    private static string CreateEventId() => $"evt_{Guid.NewGuid():N}";

    private static string CreateItemId() => $"item_{Guid.NewGuid():N}";
}
