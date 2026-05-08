# Zhengyan.DigitalWife.Audio.PortAudio

`Zhengyan.DigitalWife.Audio.PortAudio` 提供基于 `PortAudioSharp2` 的跨平台录音与播放实现，对外实现了 `IAudioSource` 与 `IAudioPlayer`。

## 主要 API

### `ServiceCollectionExtensions`

- `AddPortAudio(IServiceCollection services, PortAudioRuntimeOptions? options = null)`

注册：

- `PortAudioDeviceCatalog`
- `PortAudioMicrophoneSource`
- `PortAudioSpeakerPlayer`
- `IAudioSource`
- `IAudioPlayer`

### `PortAudioRuntimeOptions`

常用字段：

- `InputDeviceIndex`
- `OutputDeviceIndex`

### `PortAudioDeviceCatalog`

用于枚举设备：

- `ListInputDevices()`
- `ListOutputDevices()`

返回类型：

- `PortAudioDeviceDescriptor`

### `PortAudioMicrophoneSource`

实现：

- `CaptureAsync()`
- `RecordAsync()`
- `RecordUntilSilenceAsync()`

### `PortAudioSpeakerPlayer`

实现：

- `PlayAsync(AudioData audio, ...)`
- `PlayAsync(IAsyncEnumerable<AudioChunk> audioStream, AudioFormat format, ...)`
- `PlayFileAsync(string path, ...)`

## 设备枚举示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Audio.PortAudio;

ServiceCollection services = new();
services.AddPortAudio();

using ServiceProvider provider = services.BuildServiceProvider();
PortAudioDeviceCatalog catalog = provider.GetRequiredService<PortAudioDeviceCatalog>();

foreach (PortAudioDeviceDescriptor device in catalog.ListInputDevices())
{
    Console.WriteLine($"IN  [{device.Index}] {device.Name}");
}

foreach (PortAudioDeviceDescriptor device in catalog.ListOutputDevices())
{
    Console.WriteLine($"OUT [{device.Index}] {device.Name}");
}
```

## 指定输入输出设备

```csharp
services.AddPortAudio(new PortAudioRuntimeOptions
{
    InputDeviceIndex = 1,
    OutputDeviceIndex = 3
});
```

## 录音并播放

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.PortAudio;

ServiceCollection services = new();
services.AddPortAudio();

using ServiceProvider provider = services.BuildServiceProvider();
IAudioSource audioSource = provider.GetRequiredService<IAudioSource>();
IAudioPlayer audioPlayer = provider.GetRequiredService<IAudioPlayer>();

AudioData captured = await audioSource.RecordUntilSilenceAsync(new VoiceActivityCaptureOptions
{
    SampleRate = 16000,
    Channels = 1,
    SilenceTimeout = TimeSpan.FromMilliseconds(800)
});

await audioPlayer.PlayAsync(captured);
```

## 写入 WAV

```csharp
AudioData captured = await audioSource.RecordAsync(TimeSpan.FromSeconds(3));
await WaveFile.WriteAsync("test.wav", captured);
await audioPlayer.PlayFileAsync("test.wav");
```

## 适合什么场景

- 需要跨平台本地录音与播放。
- 希望在 `Assistant` 编排层中直接替换成具体音频 Provider。
- 需要按设备索引精确选择输入输出设备。
