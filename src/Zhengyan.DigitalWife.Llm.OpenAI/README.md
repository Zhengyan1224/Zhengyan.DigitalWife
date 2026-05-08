# Zhengyan.DigitalWife.Llm.OpenAI

`Zhengyan.DigitalWife.Llm.OpenAI` 提供 OpenAI 协议兼容的流式 LLM 客户端，实现了 `ILlmClient`。

## 主要 API

### `OpenAiCompatibleLlmOptions`

- `BaseUrl`
- `ApiKey`
- `ChatCompletionsPath`
- `Timeout`

### `ServiceCollectionExtensions`

- `AddOpenAiCompatibleLlmClient(IServiceCollection services, OpenAiCompatibleLlmOptions options)`

注册：

- `OpenAiCompatibleLlmClient`
- `ILlmClient`

### `OpenAiCompatibleLlmClient`

对外通过 `ILlmClient` 使用：

- `IAsyncEnumerable<LlmStreamUpdate> StreamChatAsync(IReadOnlyList<LlmChatMessage> messages, LlmRequestOptions options, CancellationToken cancellationToken = default)`

## 注册示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Llm.OpenAI;

ServiceCollection services = new();
services.AddOpenAiCompatibleLlmClient(new OpenAiCompatibleLlmOptions
{
    BaseUrl = "http://127.0.0.1:8000",
    ApiKey = "YOUR_KEY",
    ChatCompletionsPath = "/v1/chat/completions",
    Timeout = TimeSpan.FromMinutes(5)
});
```

## 流式调用示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Llm;

using ServiceProvider provider = services.BuildServiceProvider();
ILlmClient llm = provider.GetRequiredService<ILlmClient>();

List<LlmChatMessage> messages =
[
    new("system", "你是一个中文助手。"),
    new("user", "用一句话介绍 PMX 和 VMD 的区别。")
];

await foreach (LlmStreamUpdate update in llm.StreamChatAsync(
    messages,
    new LlmRequestOptions
    {
        Model = "qwen2.5-14b-instruct",
        Temperature = 0.2f
    }))
{
    Console.Write(update.Delta);
}
```

## 何时使用

- 你的模型服务兼容 OpenAI Chat Completions 协议。
- 你希望把本地模型服务、云端服务、代理网关都统一到同一套客户端接口下。
- 你需要增量输出以支持边生成边播报。
