using System.Text.Json.Serialization;

namespace Zhengyan.DigitalWife.Realtime.OpenAI;

public sealed class OpenAiAudioSpeechRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("response_format")]
    public string ResponseFormat { get; set; } = "wav";

    [JsonPropertyName("speed")]
    public float? Speed { get; set; }
}
