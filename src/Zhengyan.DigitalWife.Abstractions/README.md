# Abstractions：音频、语音与 LLM 公共接口

这个 .NET 10 类库定义不同模块之间交换的数据和接口。它不录音、不推理，也不直接调用 AI 服务；具体实现由 PortAudio、OpenAL、Sherpa、Whisper 和 LLM 客户端提供。

适合希望替换某个提供者或在自己的 C# 程序中组合语音链路的开发者。普通游戏使用者只需编辑器或播放器。

## 主要概念

| 命名空间 | 数据 / 接口 | 用途 |
| --- | --- | --- |
| `Zhengyan.DigitalWife.Audio` | AudioData、AudioChunk、AudioFormat | 音频样本、片段与格式 |
| 同上 | IAudioSource、IAudioPlayer、IAudioPlaybackTiming | 录音、播放与预计输出延迟 |
| 同上 | WaveFile | WAV 文件读写 |
| `Zhengyan.DigitalWife.Speech` | ISpeechRecognizer | ASR：语音转文字 |
| 同上 | ITextToSpeechSynthesizer | TTS：文字转语音 |
| 同上 | IWakeWordDetector | 唤醒词检测 |
| `Zhengyan.DigitalWife.Llm` | ILlmClient、LlmChatMessage、LlmRequestOptions | 对话请求与流式文本 |
| 同上 | LlmToolDefinition、LlmToolCall | 工具定义与调用信息 |

还保留旧 Android 脚本 globals 契约，用于此前导出包的兼容。新 Android 脚本使用 [Android.Scripting](../Zhengyan.DigitalWife.GamePlayer.Android.Scripting/README.md)。

## 入门使用

在自己的 .NET 10 项目中引用该库：

```text
dotnet add MyApp/MyApp.csproj reference src/Zhengyan.DigitalWife.Abstractions/Zhengyan.DigitalWife.Abstractions.csproj
```

构造一段 16 kHz 单声道静音：

```csharp
using Zhengyan.DigitalWife.Audio;

AudioData silence = new(new float[16000], new AudioFormat(16000, 1));
await WaveFile.WriteAsync("silence.wav", silence);
```

这会生成音频文件，不会播放它。想录音还需要 IAudioSource 的实现，想说话还需要 TTS 和 IAudioPlayer。

接口不是服务实例。使用依赖注入时，先注册对应实现，再用 GetRequiredService 取出接口。完整组合见 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md)。

## 构建与下一步

在仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.Abstractions
```

- [项目总览](../../README.md)
- [Assistant 编排](../Zhengyan.DigitalWife.Assistant/README.md)
- [录音与播放](../Zhengyan.DigitalWife.Audio.PortAudio/README.md)
- [LLM 客户端](../Zhengyan.DigitalWife.Llm.OpenAI/README.md)
