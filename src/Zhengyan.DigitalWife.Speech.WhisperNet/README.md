# Zhengyan.DigitalWife.Speech.WhisperNet

`Zhengyan.DigitalWife.Speech.WhisperNet` 基于 `Whisper.net` 提供 `ISpeechRecognizer` 实现。

它适合作为：

- 高兼容、易部署的离线识别 Provider
- `SherpaOnnx` 链路的回退识别器
- 需要 NVIDIA CUDA 时的可选 GPU 识别后端

## 主要 API

### `WhisperNetRecognizerOptions`

- `ModelPath`
- `Language`
- `TranslateToEnglish`
- `UseGpu`
- `Threads`
- `SampleRate`

### `ServiceCollectionExtensions`

- `AddWhisperNetSpeechRecognizer(IServiceCollection services, WhisperNetRecognizerOptions options)`

注册：

- `WhisperNetSpeechRecognizer`
- `ISpeechRecognizer`

## 注册示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech.WhisperNet;

ServiceCollection services = new();
services.AddWhisperNetSpeechRecognizer(new WhisperNetRecognizerOptions
{
    ModelPath = "models/whisper/ggml-base.bin",
    Language = "auto",
    TranslateToEnglish = false,
    UseGpu = false,
    Threads = 4,
    SampleRate = 16000
});
```

## 识别文件示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech;

using ServiceProvider provider = services.BuildServiceProvider();
ISpeechRecognizer recognizer = provider.GetRequiredService<ISpeechRecognizer>();

SpeechRecognitionResult result = await recognizer.RecognizeFileAsync(
    "test.wav",
    new SpeechRecognitionOptions
    {
        Language = "zh",
        EnableTimestamps = true
    });

Console.WriteLine(result.Text);
```

## GPU 说明

`UseGpu = true` 会请求 `Whisper.net` 优先使用 GPU runtime。

当前仓库已经引入了 `Whisper.net.Runtime.Cuda`，但是否真正加载到 CUDA 仍取决于：

- 部署平台
- NVIDIA 驱动 / CUDA 环境
- native runtime 是否可被正确加载

在 `RealtimeVoice` 服务里，启动日志会打印：

- `useGpu`
- `loadedRuntimeLibrary`
- `runtimeOrder`

如果你希望确认是否真的在用 GPU，重点看：

- `loadedRuntimeLibrary=Cuda`

## 作为回退识别器

`VoiceAssistantPipeline` 会按注册顺序依次尝试 `ISpeechRecognizer`。如果你想把 Whisper 作为回退识别器，可以先注册主识别器，再注册 Whisper：

```csharp
services.AddSherpaOnnxSpeechRecognizer(mainRecognizerOptions);
services.AddWhisperNetSpeechRecognizer(new WhisperNetRecognizerOptions
{
    ModelPath = "models/whisper/ggml-base.bin"
});
```

## 适合什么场景

- 需要离线识别但不想完全依赖 SherpaOnnx 模型族
- 需要稳定的回退识别器
- 需要在支持的环境下尝试 CUDA GPU 识别
