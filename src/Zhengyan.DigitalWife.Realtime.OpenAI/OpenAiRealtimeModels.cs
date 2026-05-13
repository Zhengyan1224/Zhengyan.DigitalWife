using System.Text.Json.Serialization;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Realtime.OpenAI;

public sealed class OpenAiRealtimeSession
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("output_modalities")]
    public IReadOnlyList<string> OutputModalities { get; set; } = ["audio"];

    [JsonPropertyName("audio")]
    public OpenAiRealtimeSessionAudioOptions Audio { get; set; } = new();

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }
}

public sealed class OpenAiRealtimeSessionAudioOptions
{
    [JsonPropertyName("input")]
    public OpenAiRealtimeSessionInputAudioOptions Input { get; set; } = new();

    [JsonPropertyName("output")]
    public OpenAiRealtimeSessionOutputAudioOptions Output { get; set; } = new();
}

public sealed class OpenAiRealtimeSessionInputAudioOptions
{
    [JsonPropertyName("format")]
    public OpenAiRealtimeAudioFormat Format { get; set; } = OpenAiRealtimeAudioFormat.Pcm16();

    [JsonPropertyName("transcription")]
    public OpenAiRealtimeInputAudioTranscription? Transcription { get; set; } = new()
    {
        Model = "whisper-1",
        Language = "zh"
    };

    [JsonPropertyName("turn_detection")]
    public OpenAiRealtimeTurnDetection? TurnDetection { get; set; }
}

public sealed class OpenAiRealtimeSessionOutputAudioOptions
{
    [JsonPropertyName("format")]
    public OpenAiRealtimeAudioFormat Format { get; set; } = OpenAiRealtimeAudioFormat.Pcm16();

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }
}

public sealed record OpenAiRealtimeAudioFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("rate")] int? Rate = null)
{
    public static OpenAiRealtimeAudioFormat Pcm16(int rate = 24_000) => new("audio/pcm", rate);

    public AudioFormat ToAudioFormat(int defaultRate = 24_000, int channels = 1)
    {
        return Type switch
        {
            "audio/pcm" => new AudioFormat(Rate ?? defaultRate, channels, AudioEncoding.Float32),
            _ => throw new NotSupportedException($"Unsupported Realtime audio format: {Type}")
        };
    }
}

public sealed class OpenAiRealtimeInputAudioTranscription
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }
}

public sealed class OpenAiRealtimeTurnDetection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "server_vad";

    [JsonPropertyName("threshold")]
    public float? Threshold { get; set; }

    [JsonPropertyName("prefix_padding_ms")]
    public int? PrefixPaddingMilliseconds { get; set; }

    [JsonPropertyName("silence_duration_ms")]
    public int? SilenceDurationMilliseconds { get; set; }

    [JsonPropertyName("idle_timeout_ms")]
    public int? IdleTimeoutMilliseconds { get; set; }
}

public sealed class OpenAiRealtimeConversationItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "completed";

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public IReadOnlyList<OpenAiRealtimeContentPart> Content { get; set; } = [];
}

public sealed class OpenAiRealtimeContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("transcript")]
    public string? Transcript { get; set; }

    [JsonPropertyName("audio")]
    public string? Audio { get; set; }
}

public sealed class OpenAiRealtimeResponseAudioOptions
{
    [JsonPropertyName("format")]
    public OpenAiRealtimeAudioFormat? Format { get; set; }

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }
}

public sealed class OpenAiRealtimeResponseRequest
{
    [JsonPropertyName("conversation")]
    public string? Conversation { get; set; } = "auto";

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("output_modalities")]
    public IReadOnlyList<string>? OutputModalities { get; set; }

    [JsonPropertyName("audio")]
    public OpenAiRealtimeResponseAudioOptions? Audio { get; set; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }
}

public sealed class OpenAiRealtimeResponseInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("output")]
    public IReadOnlyList<OpenAiRealtimeConversationItem> Output { get; set; } = [];
}

public sealed class OpenAiRealtimeError
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }
}

public sealed class OpenAiRealtimeClientEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }

    [JsonPropertyName("session")]
    public OpenAiRealtimeSession? Session { get; set; }

    [JsonPropertyName("audio")]
    public string? Audio { get; set; }

    [JsonPropertyName("item")]
    public OpenAiRealtimeConversationItem? Item { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("previous_item_id")]
    public string? PreviousItemId { get; set; }

    [JsonPropertyName("response")]
    public OpenAiRealtimeResponseRequest? Response { get; set; }
}

public sealed class OpenAiRealtimeServerEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }

    [JsonPropertyName("session")]
    public OpenAiRealtimeSession? Session { get; set; }

    [JsonPropertyName("response")]
    public OpenAiRealtimeResponseInfo? Response { get; set; }

    [JsonPropertyName("item")]
    public OpenAiRealtimeConversationItem? Item { get; set; }

    [JsonPropertyName("part")]
    public OpenAiRealtimeContentPart? Part { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("previous_item_id")]
    public string? PreviousItemId { get; set; }

    [JsonPropertyName("response_id")]
    public string? ResponseId { get; set; }

    [JsonPropertyName("output_index")]
    public int? OutputIndex { get; set; }

    [JsonPropertyName("content_index")]
    public int? ContentIndex { get; set; }

    [JsonPropertyName("delta")]
    public string? Delta { get; set; }

    [JsonPropertyName("transcript")]
    public string? Transcript { get; set; }

    [JsonPropertyName("error")]
    public OpenAiRealtimeError? Error { get; set; }
}

public sealed class OpenAiRealtimeTranscriptionResult
{
    public required string ItemId { get; init; }

    public required string Text { get; init; }

    public OpenAiRealtimeConversationItem? Item { get; init; }
}

public sealed class OpenAiRealtimeResponseUpdate
{
    public string? ResponseId { get; init; }

    public string? AssistantItemId { get; init; }

    public string? TranscriptDelta { get; init; }

    public string AssistantTranscript { get; init; } = string.Empty;

    public AudioChunk? AudioChunk { get; init; }

    public bool IsStarted { get; init; }

    public bool IsCompleted { get; init; }

    public string? Status { get; init; }

    public string? FinalAssistantText { get; init; }
}
