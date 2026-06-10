using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        string url = CombineUrl(_options.BaseUrl, _options.ChatCompletionsPath);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = messages.Select(CreateMessagePayload).ToArray(),
            ["stream"] = true
        };

        if (options.Temperature.HasValue)
        {
            payload["temperature"] = options.Temperature.Value;
        }

        if (options.Tools is { Count: > 0 })
        {
            payload["tools"] = options.Tools.Select(CreateToolPayload).ToArray();
        }

        if (options.AdditionalProperties is not null)
        {
            foreach (KeyValuePair<string, object?> pair in options.AdditionalProperties)
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
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {TrimBody(body)}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var accumulated = new StringBuilder();
        var toolCallBuilders = new List<ToolCallBuilder>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string data = line["data:".Length..].Trim();
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                yield return new LlmStreamUpdate
                {
                    Delta = string.Empty,
                    AccumulatedText = accumulated.ToString(),
                    IsFinal = true,
                    ToolCalls = BuildToolCalls(toolCallBuilders)
                };

                yield break;
            }

            ChatCompletionChunk? chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions);
            Delta? responseDelta = chunk?.Choices?.FirstOrDefault()?.Delta;
            if (responseDelta is null)
            {
                continue;
            }

            AppendToolCalls(toolCallBuilders, responseDelta.ToolCalls);
            string delta = responseDelta.Content ?? string.Empty;
            IReadOnlyList<LlmToolCall> toolCalls = BuildToolCalls(toolCallBuilders);
            if (string.IsNullOrEmpty(delta) && toolCalls.Count == 0)
            {
                continue;
            }

            accumulated.Append(delta);
            yield return new LlmStreamUpdate
            {
                Delta = delta,
                AccumulatedText = accumulated.ToString(),
                IsFinal = false,
                ToolCalls = toolCalls
            };
        }

        yield return new LlmStreamUpdate
        {
            Delta = string.Empty,
            AccumulatedText = accumulated.ToString(),
            IsFinal = true,
            ToolCalls = BuildToolCalls(toolCallBuilders)
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
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && (string.Equals(absolute.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(absolute.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            return absolute.ToString();
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
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

    private static Dictionary<string, object?> CreateMessagePayload(LlmChatMessage message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };

        if (!string.IsNullOrWhiteSpace(message.ToolCallId))
        {
            payload["tool_call_id"] = message.ToolCallId;
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            payload["tool_calls"] = message.ToolCalls
                .Select(call => new Dictionary<string, object?>
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.ArgumentsJson
                    }
                })
                .ToArray();
        }

        return payload;
    }

    private static Dictionary<string, object?> CreateToolPayload(LlmToolDefinition tool)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = ParseJsonSchema(tool.ParametersJsonSchema)
            }
        };
    }

    private static object ParseJsonSchema(string jsonSchema)
    {
        if (string.IsNullOrWhiteSpace(jsonSchema))
        {
            return CreateEmptyObjectSchema();
        }

        try
        {
            JsonNode? node = JsonNode.Parse(jsonSchema);
            return node is null ? CreateEmptyObjectSchema() : node;
        }
        catch (JsonException)
        {
            return CreateEmptyObjectSchema();
        }
    }

    private static Dictionary<string, object?> CreateEmptyObjectSchema()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>()
        };
    }

    private static void AppendToolCalls(List<ToolCallBuilder> builders, List<ToolCallDelta>? deltas)
    {
        if (deltas is null)
        {
            return;
        }

        foreach (ToolCallDelta delta in deltas)
        {
            int index = delta.Index ?? builders.Count;
            while (builders.Count <= index)
            {
                builders.Add(new ToolCallBuilder());
            }

            ToolCallBuilder builder = builders[index];
            if (!string.IsNullOrWhiteSpace(delta.Id))
            {
                builder.Id = delta.Id;
            }

            if (!string.IsNullOrWhiteSpace(delta.Function?.Name))
            {
                builder.Name = delta.Function.Name;
            }

            if (delta.Function?.Arguments is not null)
            {
                builder.Arguments.Append(delta.Function.Arguments);
            }
        }
    }

    private static IReadOnlyList<LlmToolCall> BuildToolCalls(List<ToolCallBuilder> builders)
    {
        return builders
            .Where(builder => !string.IsNullOrWhiteSpace(builder.Name))
            .Select((builder, index) => new LlmToolCall(
                string.IsNullOrWhiteSpace(builder.Id) ? $"call_{index}" : builder.Id,
                builder.Name,
                builder.Arguments.ToString()))
            .ToArray();
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

        public List<ToolCallDelta>? ToolCalls { get; init; }
    }

    private sealed class ToolCallDelta
    {
        public int? Index { get; init; }

        public string? Id { get; init; }

        public ToolCallFunctionDelta? Function { get; init; }
    }

    private sealed class ToolCallFunctionDelta
    {
        public string? Name { get; init; }

        public string? Arguments { get; init; }
    }

    private sealed class ToolCallBuilder
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public StringBuilder Arguments { get; } = new();
    }
}
