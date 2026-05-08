namespace Zhengyan.DigitalWife.Llm.OpenAI;

public sealed class OpenAiCompatibleLlmOptions
{
    public required string BaseUrl { get; init; }

    public required string ApiKey { get; init; }

    public string ChatCompletionsPath { get; init; } = "/v1/chat/completions";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

