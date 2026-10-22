# Llm.OpenAI：兼容 Chat Completions 的文本客户端

这个类库连接提供 OpenAI-compatible Chat Completions 接口的 LLM 服务，返回流式文本和工具调用信息。这里的 OpenAI 是协议适配名称，不要求只能连接某一家供应商。

它不部署 LLM，不做语音识别，也不自动执行工具。完整游戏工具循环由 GamePlayer 的 RuntimeLlm 管理。

## 需要准备什么

- 一个已启动且可访问的兼容服务。
- 正确的 BaseUrl、ChatCompletionsPath 和 Model。
- 服务需要鉴权时提供 ApiKey。
- .NET 10 程序，或在 GameEditor 中配置 LLM。

模型名必须是服务实际接受的名称，不能照抄示例模型名后假设已安装。

## C# 最小示例

在引用本库的程序中：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Llm.OpenAI;

var services = new ServiceCollection();
services.AddOpenAiCompatibleLlmClient(new OpenAiCompatibleLlmOptions
{
    BaseUrl = "http://127.0.0.1:8000",
    ChatCompletionsPath = "/v1/chat/completions",
    ApiKey = "YOUR_KEY",
    Model = "YOUR_MODEL"
});
using var provider = services.BuildServiceProvider();
ILlmClient client = provider.GetRequiredService<ILlmClient>();
await foreach (LlmStreamUpdate update in client.StreamChatAsync(
    new[] { new LlmChatMessage("user", "你好") },
    new LlmRequestOptions { Model = "YOUR_MODEL" }))
{
    Console.Write(update.Delta);
}
```

执行前替换地址、密钥和模型。无鉴权的服务按它的要求配置，不需要编造密钥。

## 流式与工具

- Delta 是新增文本，AccumulatedText 是累计文本。
- Tools 提供 JSON Schema 工具定义。
- 工具调用返回 ID、名称与参数 JSON，调用方负责验证、执行和回传结果。
- Timeout、CancellationToken 与服务 HTTP 错误保留在调用链中。

某些兼容服务不支持工具字段，或输出格式与预期不同；游戏运行层有适配与回退逻辑，本库不能承诺所有兼容服务都支持同样功能。

## 排查连接问题

先检查最终接口路径、端口、鉴权和模型名。手机或容器里的 127.0.0.1 指它们自己，不是运行编辑器的电脑。

检查异常中的 HTTP 状态和响应信息，反馈时隐去 API Key。调用服务可能产生费用，具体由所接入的服务决定。

```text
dotnet build src/Zhengyan.DigitalWife.Llm.OpenAI
```

- [项目总览](../../README.md)
- [GamePlayer LLM 脚本](../Zhengyan.DigitalWife.GameEditor/api_docs/25-llm.md)
- [语音服务示例](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
