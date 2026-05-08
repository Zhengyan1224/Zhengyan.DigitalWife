namespace Zhengyan.DigitalWife.Llm;

public sealed record LlmChatMessage(string Role, string Content);

public sealed class LlmRequestOptions
{
    public required string Model { get; init; }

    public float? Temperature { get; init; }

    public IDictionary<string, object?>? AdditionalProperties { get; init; }
}

public sealed class LlmStreamUpdate
{
    public required string Delta { get; init; }

    public required string AccumulatedText { get; init; }

    public bool IsFinal { get; init; }
}

public interface ILlmClient
{
    IAsyncEnumerable<LlmStreamUpdate> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmRequestOptions options,
        CancellationToken cancellationToken = default);
}

