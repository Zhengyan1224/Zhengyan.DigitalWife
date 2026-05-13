# Zhengyan.DigitalWife.Llm.OpenAI

`Zhengyan.DigitalWife.Llm.OpenAI` 提供 OpenAI 兼容 `Chat Completions` 流式客户端，实现了 `ILlmClient`。

## 主要 API

### `OpenAiCompatibleLlmOptions`

- `BaseUrl`
- `ApiKey`
- `Model`
- `ChatCompletionsPath`
- `Timeout`

说明：

- `Model` 是可选的默认模型字段，便于上层 sample 把模型名与 LLM 服务地址归到同一个配置节点
- 实际调用时，`ILlmClient` 仍然通过 `LlmRequestOptions.Model` 决定发给后端的模型名

### `ServiceCollectionExtensions`

- `AddOpenAiCompatibleLlmClient(IServiceCollection services, OpenAiCompatibleLlmOptions options)`

注册：

- `OpenAiCompatibleLlmClient`
- `ILlmClient`

## 注册示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Llm.OpenAI;

ServiceCollection services = new();
services.AddOpenAiCompatibleLlmClient(new OpenAiCompatibleLlmOptions
{
    BaseUrl = "http://127.0.0.1:8000",
    ApiKey = "YOUR_KEY",
    Model = "qwen2.5-14b-instruct",
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

## 错误信息

如果上游返回非 2xx 状态码，当前实现会把响应体内容一起带进异常信息，便于排查网关、模型服务或鉴权错误。

## 适合什么场景

- 你的模型服务兼容 OpenAI `Chat Completions`
- 你希望统一对接本地模型服务、云端服务、代理网关
- 你需要增量输出以支持边生成边播报
