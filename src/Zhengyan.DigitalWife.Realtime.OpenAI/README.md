# Realtime.OpenAI：语音会话协议客户端

这个类库处理 Realtime 风格 WebSocket 会话、音频编码、对话项、转写、回复事件和 HTTP 文本转语音请求。它是协议层，不操作麦克风或扬声器。

可连接仓库中的 [RealtimeVoice 服务](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)，也可用于实现了匹配协议的服务。这里不是对所有 Realtime 服务与全部特性的兼容承诺。

## 主要接口

| 方法 / 类型 | 用途 |
| --- | --- |
| OpenAiRealtimeClientOptions | 服务根地址、路径、密钥和连接参数 |
| ConnectAsync / UpdateSessionAsync | 建立连接与配置会话 |
| TranscribeAsync | 音频转写 |
| CreateConversationItemAsync | 添加用户文本等会话项 |
| CreateResponseAsync | 读取流式回复文本 / 音频事件 |
| DeleteConversationItemAsync / ResetConversationAsync | 管理对话上下文 |
| SynthesizeTextAsync | 调用 HTTP audio/speech |
| OpenAiRealtimeSession / ResponseRequest | 协议数据模型 |

## 建立会话示例

以下示例假定本地语音服务已启动在 5058，语音模型与 LLM 已配置：

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Zhengyan.DigitalWife.Realtime.OpenAI;

await using var client = new OpenAiRealtimeClient(
    new OpenAiRealtimeClientOptions { BaseUrl = "http://127.0.0.1:5058" },
    NullLogger<OpenAiRealtimeClient>.Instance);
await client.ConnectAsync();
await client.UpdateSessionAsync(new OpenAiRealtimeSession
{
    Model = "zhengyan-realtime-voice",
    Instructions = "请用简洁中文回答。"
});
```

后续把 IAudioSource 录到的 AudioData 交给 TranscribeAsync，或创建用户会话项再请求回复。音频播放和口型仍由前端完成。现成前端见 [DigitalHuman](../../samples/Zhengyan.DigitalWife.Samples.DigitalHuman/README.md)。

## 地址与音频

- HTTP / HTTPS 根地址分别用于 WS / WSS 会话连接。
- 默认 WebSocket 路径是 /v1/realtime，TTS 路径是 /v1/audio/speech。
- PCM 采样率和服务会话参数需要匹配；本地声卡采样率可以先重采样后再发送。
- ApiKey 与 Headers 是接入服务的鉴权信息；不要写进公开示例或日志。
- 输出事件与音频缓冲不同，收到 final 标识不等于扬声器已播完。

## 构建与下一步

在仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.Realtime.OpenAI
```

如果只想在引擎中调用，不需要自己处理协议；使用 [Scene.RealtimeVoice](../Zhengyan.DigitalWife.GameEditor/api_docs/26-realtime-voice.md) 或 [RealtimeVoice.Client](../Zhengyan.DigitalWife.RealtimeVoice.Client/README.md)。

- [项目总览](../../README.md)
- [语音服务启动](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
