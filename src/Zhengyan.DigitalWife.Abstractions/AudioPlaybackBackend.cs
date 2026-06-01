using System.Text.Json.Serialization;

namespace Zhengyan.DigitalWife.Audio;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioPlaybackBackend
{
    PortAudio,
    OpenAL
}
