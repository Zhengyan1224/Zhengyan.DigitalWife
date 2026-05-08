# Zhengyan.DigitalWife.Speech.WhisperNet

`Zhengyan.DigitalWife.Speech.WhisperNet` 基于 `Whisper.net` 提供 `ISpeechRecognizer` 实现，适合作为高兼容、易部署的离线识别 Provider，也适合作为 `SherpaOnnx` 识别链路的回退方案。

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

- 需要离线识别但不想依赖 SherpaOnnx 模型族。
- 需要一个稳定的回退识别器。
- 需要快速验证本地识别链路是否可用。
