using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Realtime.OpenAI;

public static class OpenAiRealtimeProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeClientEvent(OpenAiRealtimeClientEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static string SerializeServerEvent(OpenAiRealtimeServerEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static OpenAiRealtimeClientEvent DeserializeClientEvent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<OpenAiRealtimeClientEvent>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize Realtime client event.");
    }

    public static OpenAiRealtimeServerEvent DeserializeServerEvent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<OpenAiRealtimeServerEvent>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize Realtime server event.");
    }

    public static AudioData PrepareAudio(AudioData audio, OpenAiRealtimeAudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(format);

        return format.Type switch
        {
            "audio/pcm" => audio.ToMono().Resample(format.Rate ?? 24_000),
            _ => throw new NotSupportedException($"Unsupported Realtime audio format: {format.Type}")
        };
    }

    public static string EncodePcm16(AudioData audio, OpenAiRealtimeAudioFormat format)
    {
        AudioData normalized = PrepareAudio(audio, format);
        byte[] buffer = new byte[normalized.Samples.Length * sizeof(short)];

        for (int i = 0; i < normalized.Samples.Length; i++)
        {
            short sample = (short)Math.Round(Math.Clamp(normalized.Samples[i], -1.0f, 1.0f) * short.MaxValue);
            BitConverter.TryWriteBytes(buffer.AsSpan(i * sizeof(short), sizeof(short)), sample);
        }

        return Convert.ToBase64String(buffer);
    }

    public static AudioData DecodePcm16(string base64, OpenAiRealtimeAudioFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        ArgumentNullException.ThrowIfNull(format);

        if (!string.Equals(format.Type, "audio/pcm", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported Realtime audio format: {format.Type}");
        }

        byte[] raw = Convert.FromBase64String(base64);
        int sampleCount = raw.Length / sizeof(short);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short value = BitConverter.ToInt16(raw, i * sizeof(short));
            samples[i] = value / (float)short.MaxValue;
        }

        return new AudioData(samples, new AudioFormat(format.Rate ?? 24_000, 1, AudioEncoding.Float32));
    }

    public static string ExtractText(OpenAiRealtimeConversationItem? item)
    {
        if (item?.Content is null || item.Content.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (OpenAiRealtimeContentPart part in item.Content)
        {
            string? segment = part.Text ?? part.Transcript;
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(segment.Trim());
        }

        return builder.ToString();
    }
}
