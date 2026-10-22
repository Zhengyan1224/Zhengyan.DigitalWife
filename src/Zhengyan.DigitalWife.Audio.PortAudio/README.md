# Audio.PortAudio：跨平台麦克风与音频播放

这个库提供 PortAudio 录音与播放实现，供桌面语音助手、数字人和 PC 播放器使用。它不是语音识别器；录到的样本还需要交给 ASR。

Android 播放器使用平台 AudioRecord / AudioTrack，不直接复用桌面的设备索引。

## 提供的入口

| 类型 / 方法 | 用途 |
| --- | --- |
| PortAudioDeviceCatalog | 枚举录音与播放设备 |
| PortAudioRuntimeOptions | 选择输入/输出设备索引 |
| PortAudioMicrophoneSource | IAudioSource 录音 |
| PortAudioSpeakerPlayer | IAudioPlayer 播放 |
| AddPortAudioInput / Output / AddPortAudio | 注册实现 |
| IAudioPlaybackTiming | 预计输出延迟，用于同步口型 |

## 先确认设备

在仓库根目录执行：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --list-devices
```

这个模式列出设备而不执行对话。示例配置文件需要存在。设备列表重点看名称、Index、输入通道数与默认采样率。

也可在 C# 中读取：

```csharp
using Zhengyan.DigitalWife.Audio.PortAudio;

var catalog = new PortAudioDeviceCatalog();
foreach (var device in catalog.ListInputDevices())
    Console.WriteLine($"{device.Index}: {device.Name}, {device.DefaultSampleRate} Hz");
```

PortAudio 索引不是 Linux ALSA 的声卡号，也不是 Android 音频设备 ID。换电脑或插拔设备后索引可能变化。

## 在自己的程序中注册

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Audio.PortAudio;

var services = new ServiceCollection();
services.AddPortAudio(new PortAudioRuntimeOptions
{
    InputDeviceIndex = null,  // 系统默认；需要时替换为枚举得到的索引
    OutputDeviceIndex = null
});
```

可以只注册录音，再用 OpenAL 播放；不要同时把两个播放后端当成同一个 IAudioPlayer 注册而忽略选择顺序。

## 常见问题

- 先用系统录音工具确认麦克风正常，再检查本库。
- paInvalidSampleRate 通常是设备不支持请求采样率；试设备默认值，必要时 44100 / 48000 Hz。
- 接入协议前可重采样；协议 16 / 24 kHz 不要求声卡原生支持同一采样率。
- Linux 需要 libportaudio 与音频系统配置，见[系统依赖](../../README.md#linux-运行依赖)。
- Device busy 先检查其它程序占用，不要仅修改播放后端排查录音。

完整使用流程见 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md) 和 [DigitalHuman](../../samples/Zhengyan.DigitalWife.Samples.DigitalHuman/README.md)。

```text
dotnet build src/Zhengyan.DigitalWife.Audio.PortAudio
```

- [项目总览](../../README.md)
- [OpenAL 播放](../Zhengyan.DigitalWife.Audio.OpenAL/README.md)
