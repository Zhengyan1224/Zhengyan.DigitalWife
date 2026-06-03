using Zhengyan.DigitalWife.Realtime.OpenAI;

namespace Zhengyan.DigitalWife.RealtimeVoice.Client;

public sealed class RealtimeVoiceClientSettings
{
    public required OpenAiRealtimeClientOptions ClientOptions { get; init; }

    public required OpenAiRealtimeSession Session { get; init; }
}
