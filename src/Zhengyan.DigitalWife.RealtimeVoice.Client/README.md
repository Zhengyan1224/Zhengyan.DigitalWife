# RealtimeVoice.Client：可复用的语音服务调用封装

这个库在 Realtime.OpenAI 协议客户端上提供自动连接、会话准备、转写、创建文本项、回复、重置与 TTS 的统一入口。GamePlayer 的远端语音运行逻辑会使用它。

它不录音、不播放，也不部署语音模型；输入 AudioData 与输出音频由宿主管理。

## 何时使用

- 游戏脚本：优先使用 Scene.RealtimeVoice，见[脚本文档](../Zhengyan.DigitalWife.GameEditor/api_docs/26-realtime-voice.md)。
- 自己编写 C# 语音前端：可以直接使用本库。
- 实现更底层协议：查看 [Realtime.OpenAI](../Zhengyan.DigitalWife.Realtime.OpenAI/README.md)。

## 最小连接示例

需要已配置并运行的 RealtimeVoice 服务：

```csharp
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.RealtimeVoice.Client;

await using var client = new RealtimeVoiceClient(new RealtimeVoiceClientSettings
{
    ClientOptions = new OpenAiRealtimeClientOptions
    {
        BaseUrl = "http://127.0.0.1:5058"
    },
    Session = new OpenAiRealtimeSession
    {
        Model = "zhengyan-realtime-voice",
        Instructions = "请用简短中文回答。"
    }
});
await client.EnsureReadyAsync();
```

地址与 Model 必须匹配你的服务。手机不应直接用电脑的 127.0.0.1，除非配置了 adb reverse。

## 常用方法

| 方法 | 用途 |
| --- | --- |
| EnsureReadyAsync | 连接并初始化会话 |
| UpdateSessionAsync | 更新语音会话配置 |
| TranscribeAsync | 转写 AudioData，可删除临时会话项 |
| CreateUserTextConversationItemAsync | 添加用户文本 |
| CreateResponseAsync | 接收流式回复 |
| DeleteConversationItemAsync / ResetConversationAsync | 删除或重置上下文 |
| SynthesizeTextAsync | 固定文本直接合成语音 |

保留同一客户端可复用会话；取消和释放由调用方管理。不要把一个 final 事件当成音频播放完毕，需要等宿主播放器完成。

## 构建

仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.RealtimeVoice.Client
```

- [项目总览](../../README.md)
- [语音服务](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
- [Android 采音与播放边界](../Zhengyan.DigitalWife.GamePlayer.Android/README.md)
