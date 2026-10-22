# Audio.OpenAL：语音音频播放后端

这个类库把合成或接收到的音频交给 OpenAL 播放，提供 IAudioPlayer 和 IAudioPlaybackTiming。它只负责播放，不录音、不识别语音，也不调用 LLM。

适合与 PortAudio 麦克风输入组合，作为桌面语音助手的输出后端。Mmd.Game 的场景音频组件与这个公共接口适配库不是同一个 API 层。

## 注册示例

在引用本库的 .NET 10 程序中：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Audio.OpenAL;

var services = new ServiceCollection();
services.AddOpenAlAudioPlayer();
```

之后用 provider 获取 IAudioPlayer 播放 AudioData 或 AudioChunk。完整设备、录音和模型配置见 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md)。

## 参数与边界

OpenAlRuntimeOptions 提供预计输出延迟、最大排队缓冲数和轮询间隔，用于播放与口型协调。预计延迟不是对每个声卡的精确测量。

选择 OpenAL 时，PortAudio 的 OutputDeviceIndex 不再决定说话输出设备；录音仍由 PortAudio 配置控制。Linux 需有 OpenAL 运行库，Android 播放器使用自己的原生音频适配。

## 构建

仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.Audio.OpenAL
```

- [项目总览](../../README.md)
- [PortAudio 录音 / 播放](../Zhengyan.DigitalWife.Audio.PortAudio/README.md)
- [公共音频接口](../Zhengyan.DigitalWife.Abstractions/README.md)
