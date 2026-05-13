# Zhengyan.DigitalWife.Realtime.OpenAI

`Zhengyan.DigitalWife.Realtime.OpenAI` 提供仓库内部共享的 OpenAI 风格 Realtime 协议模型与客户端封装。

它的定位不是完整通用 SDK，而是为当前仓库里的前端样例和后端样例提供统一的协议边界：

- `samples/Zhengyan.DigitalWife.Samples.DigitalHuman`
- `samples/Zhengyan.DigitalWife.Samples.RealtimeVoice`

## 包含内容

- `OpenAiRealtimeClient`
- `OpenAiRealtimeClientOptions`
- `OpenAiRealtimeSession`
- `OpenAiRealtimeConversationItem`
- `OpenAiRealtimeResponseRequest`
- `OpenAiAudioSpeechRequest`
- `OpenAiRealtimeProtocol`

## 当前能力

`OpenAiRealtimeClient` 当前覆盖：

- 建立 `/v1/realtime` WebSocket 连接
- `session.update`
- `input_audio_buffer.append / commit`
- `conversation.item.create / delete`
- `response.create`
- 流式接收转写文本
- 流式接收回复文本和音频
- 调用 `/v1/audio/speech` 做文本直出 TTS

## 主要 API

### `OpenAiRealtimeClientOptions`

- `BaseUrl`
- `RealtimePath`
- `AudioSpeechPath`
- `ApiKey`
- `Model`
- `ConnectTimeout`
- `OutboundAudioChunkSamples`
- `Headers`

### `OpenAiRealtimeClient`

- `ConnectAsync(...)`
- `UpdateSessionAsync(...)`
- `TranscribeAsync(...)`
- `CreateResponseAsync(...)`
- `CreateConversationItemAsync(...)`
- `DeleteConversationItemAsync(...)`
- `ResetConversationAsync(...)`
- `SynthesizeTextAsync(...)`

## 最小用法

```csharp
OpenAiRealtimeClient client = serviceProvider.GetRequiredService<OpenAiRealtimeClient>();

await client.ConnectAsync(cancellationToken);
await client.UpdateSessionAsync(session, cancellationToken);

OpenAiRealtimeTranscriptionResult transcription =
    await client.TranscribeAsync(audio, cancellationToken: cancellationToken);

await foreach (OpenAiRealtimeResponseUpdate update in client.CreateResponseAsync(cancellationToken: cancellationToken))
{
    if (!string.IsNullOrWhiteSpace(update.TranscriptDelta))
    {
        Console.Write(update.TranscriptDelta);
    }

    if (update.AudioChunk is not null)
    {
        // 播放或缓存音频
    }
}
```

## 文本直出 TTS

如果你不需要经过 LLM 改写文本，而是只想“把这句文本直接合成语音”，可以调用：

```csharp
AudioData audio = await client.SynthesizeTextAsync(
    "我在，请说。",
    new OpenAiAudioSpeechRequest
    {
        Model = "zhengyan-realtime-voice",
        Voice = "0",
        ResponseFormat = "wav"
    },
    cancellationToken);
```

这会调用配置里的 `AudioSpeechPath`，默认是：

- `/v1/audio/speech`

## 说明

- `Realtime` 和 `audio/speech` 是两条独立接口线，当前客户端同时支持这两者。
- 如果后续接入新的 OpenAI 兼容供应商，优先在这个库里扩展兼容层，而不是让每个 sample 各自维护一套协议模型。
