namespace Zhengyan.DigitalWife.Llm;

public sealed record LlmChatMessage(string Role, string Content)
{
    public string? ToolCallId { get; init; }

    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }
}

public sealed record LlmToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema);

public sealed record LlmToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

public sealed class LlmRequestOptions
{
    public required string Model { get; init; }

    public float? Temperature { get; init; }

    public IReadOnlyList<LlmToolDefinition>? Tools { get; init; }

    public IDictionary<string, object?>? AdditionalProperties { get; init; }
}

public sealed class LlmStreamUpdate
{
    public required string Delta { get; init; }

    public required string AccumulatedText { get; init; }

    public bool IsFinal { get; init; }

    public IReadOnlyList<LlmToolCall> ToolCalls { get; init; } = [];
}

public interface ILlmClient
{
    IAsyncEnumerable<LlmStreamUpdate> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmRequestOptions options,
        CancellationToken cancellationToken = default);
}
