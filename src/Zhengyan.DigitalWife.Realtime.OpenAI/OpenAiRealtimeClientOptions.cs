using System.Collections.ObjectModel;
using System.Net.WebSockets;

namespace Zhengyan.DigitalWife.Realtime.OpenAI;

public sealed class OpenAiRealtimeClientOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:5058";

    public string RealtimePath { get; init; } = "/v1/realtime";

    public string AudioSpeechPath { get; init; } = "/v1/audio/speech";

    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int OutboundAudioChunkSamples { get; init; } = 4_096;

    public bool SendOpenAiBetaHeader { get; init; } = true;

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public Uri BuildEndpoint()
    {
        Uri endpoint = TryBuildAbsoluteEndpoint(RealtimePath)
            ?? TryBuildRelativeEndpoint()
            ?? throw new InvalidOperationException("Unable to build the Realtime endpoint URI.");

        endpoint = RewriteRealtimeScheme(endpoint);
        if (string.IsNullOrWhiteSpace(Model))
        {
            return endpoint;
        }

        UriBuilder builder = new(endpoint);
        string separator = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : "&";
        builder.Query = $"{builder.Query.TrimStart('?')}{separator}model={Uri.EscapeDataString(Model)}";
        return builder.Uri;
    }

    public Uri BuildAudioSpeechEndpoint()
    {
        return TryBuildAbsoluteEndpoint(AudioSpeechPath)
            ?? TryBuildRelativeEndpoint(AudioSpeechPath)
            ?? throw new InvalidOperationException("Unable to build the audio speech endpoint URI.");
    }

    public void ApplyTo(ClientWebSocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            options.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
        }

        if (SendOpenAiBetaHeader)
        {
            options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
        }

        foreach ((string header, string value) in Headers)
        {
            if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            options.SetRequestHeader(header, value);
        }
    }

    private Uri? TryBuildAbsoluteEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute) ? absolute : null;
    }

    private Uri? TryBuildRelativeEndpoint()
        => TryBuildRelativeEndpoint(RealtimePath);

    private Uri? TryBuildRelativeEndpoint(string path)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            return null;
        }

        string relativePath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.TrimStart('/');
        return new Uri(baseUri, relativePath);
    }

    private static Uri RewriteRealtimeScheme(Uri uri)
    {
        return uri.Scheme switch
        {
            "http" => new UriBuilder(uri) { Scheme = "ws", Port = uri.Port }.Uri,
            "https" => new UriBuilder(uri) { Scheme = "wss", Port = uri.Port }.Uri,
            "ws" or "wss" => uri,
            _ => throw new InvalidOperationException($"Unsupported endpoint scheme: {uri.Scheme}")
        };
    }
}
