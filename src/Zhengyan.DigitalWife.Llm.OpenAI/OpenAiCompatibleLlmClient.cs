using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.Llm;

namespace Zhengyan.DigitalWife.Llm.OpenAI;

public sealed class OpenAiCompatibleLlmClient : ILlmClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleLlmOptions _options;
    private readonly ILogger<OpenAiCompatibleLlmClient> _logger;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public OpenAiCompatibleLlmClient(
        OpenAiCompatibleLlmOptions options,
        ILogger<OpenAiCompatibleLlmClient> logger,
        HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _httpClient.Timeout = _options.Timeout;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async IAsyncEnumerable<LlmStreamUpdate> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmRequestOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);

        var url = CombineUrl(_options.BaseUrl, _options.ChatCompletionsPath);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = messages.Select(m => new Dictionary<string, string>
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray(),
            ["stream"] = true
        };

        if (options.Temperature.HasValue)
        {
            payload["temperature"] = options.Temperature.Value;
        }

        if (options.AdditionalProperties is not null)
        {
            foreach (var pair in options.AdditionalProperties)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        _logger.LogInformation("Sending streaming chat completion request to {Url}.", url);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var accumulated = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                yield return new LlmStreamUpdate
                {
                    Delta = string.Empty,
                    AccumulatedText = accumulated.ToString(),
                    IsFinal = true
                };

                yield break;
            }

            var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions);
            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (string.IsNullOrEmpty(delta))
            {
                continue;
            }

            accumulated.Append(delta);
            yield return new LlmStreamUpdate
            {
                Delta = delta,
                AccumulatedText = accumulated.ToString(),
                IsFinal = false
            };
        }

        yield return new LlmStreamUpdate
        {
            Delta = string.Empty,
            AccumulatedText = accumulated.ToString(),
            IsFinal = true
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private sealed class ChatCompletionChunk
    {
        public List<Choice>? Choices { get; init; }
    }

    private sealed class Choice
    {
        public Delta? Delta { get; init; }
    }

    private sealed class Delta
    {
        public string? Content { get; init; }
    }
}

