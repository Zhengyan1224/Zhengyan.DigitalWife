# Assistant：把录音、识别、回复和说话串起来

Assistant 是语音助手编排库。它连接公共接口完成“录音 → ASR → LLM → 分句 → TTS → 播放”，让应用不必重复编写整条流水线。

这个类库不提供模型权重，也不创建游戏窗口。最快的运行入口是 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md)。

## 核心组成

- `VoiceAssistantPipeline.RunTurnAsync`：执行一轮语音交互。
- `VoiceAssistantTurnOptions`：系统提示、历史、录音、识别、LLM 与 TTS 参数。
- `VoiceAssistantTurnResult`：用户文本、回复文本和已说出的句子。
- `SentenceChunker`：把流式文本切成适合合成语音的片段。
- `AddDigitalWifeAssistantCore`：注册编排层。

Pipeline 接收多个识别器，按注册顺序尝试回退；语音识别成功后请求 LLM。分句让回复可以逐段合成与播放，但它不是所有模型的真正逐采样点流式推理。

## 组合时需要哪些模块

| 环节 | 仓库中的实现 |
| --- | --- |
| 麦克风 | PortAudio |
| 语音转文字 | SherpaOnnx / WhisperNet |
| 文本回复 | Llm.OpenAI |
| 文字转语音 | SherpaOnnx |
| 播放 | PortAudio / OpenAL |

使用 Microsoft.Extensions.DependencyInjection 时先注册编排和各个提供者，之后获取 VoiceAssistantPipeline。服务注册不等于模型已下载或设备已成功打开。

## 调用一轮

以下片段假定 provider 已注册了上述提供者，且 myModelName 是实际 LLM 服务支持的模型名：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Assistant.Conversation;
using Zhengyan.DigitalWife.Llm;

VoiceAssistantPipeline pipeline = provider.GetRequiredService<VoiceAssistantPipeline>();
VoiceAssistantTurnResult result = await pipeline.RunTurnAsync(new VoiceAssistantTurnOptions
{
    SystemPrompt = "请用简短自然的中文回答。",
    LlmOptions = new LlmRequestOptions { Model = myModelName }
});
Console.WriteLine(result.UserText);
Console.WriteLine(result.AssistantText);
```

录音设备、语音模型、输出播放器和历史管理都由调用方配置。唤醒词检测属于另一个接口，不会因为调用 RunTurnAsync 自动持续监听。

## 运行示例与构建

仓库根目录：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --run-once
dotnet build src/Zhengyan.DigitalWife.Assistant
```

先按示例 README 下载模型并填写本地配置，不能用未替换的服务地址启动完整对话。

- [项目总览](../../README.md)
- [公共接口](../Zhengyan.DigitalWife.Abstractions/README.md)
- [服务端语音示例](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
