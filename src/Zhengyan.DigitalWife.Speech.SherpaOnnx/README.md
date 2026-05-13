# Zhengyan.DigitalWife.Speech.SherpaOnnx

`Zhengyan.DigitalWife.Speech.SherpaOnnx` 基于 `org.k2fsa.sherpa.onnx` 提供三类能力：

- 语音识别 `ISpeechRecognizer`
- 语音合成 `ITextToSpeechSynthesizer`
- 唤醒词检测 `IWakeWordDetector`

## 主要 API

### 识别

- `SherpaOnnxRecognizerModelKind`
- `SherpaOnnxRecognizerOptions`
- `SherpaOnnxSpeechRecognizer`
- `AddSherpaOnnxSpeechRecognizer(...)`

`SherpaOnnxRecognizerOptions` 常用字段：

- `ModelKind`
- `TokensPath`
- `EncoderPath`
- `DecoderPath`
- `JoinerPath`
- `ModelPath`
- `Language`
- `Provider`
- `SampleRate`
- `FeatureDim`
- `Threads`
- `DecodingMethod`

### TTS

- `SpeechSynthesisModelKind`
- `SherpaOnnxTtsOptions`
- `SherpaOnnxTextToSpeechSynthesizer`
- `AddSherpaOnnxTextToSpeech(...)`

`SherpaOnnxTtsOptions` 常用字段：

- `ModelPath`
- `TokensPath`
- `ModelKind`
- `LexiconPath`
- `DataDirectory`
- `DictDirectory`
- `VocoderPath`
- `RuleFars`
- `RuleFsts`
- `Provider`
- `Threads`
- `NoiseScale`
- `NoiseScaleW`
- `LengthScale`

### 唤醒词

- `SherpaOnnxWakeWordOptions`
- `SherpaOnnxWakeWordDetector`
- `AddSherpaOnnxWakeWordDetector(...)`

## 注册识别器

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech;

ServiceCollection services = new();
services.AddSherpaOnnxSpeechRecognizer(new SherpaOnnxRecognizerOptions
{
    ModelKind = SherpaOnnxRecognizerModelKind.OnlineTransducer,
    TokensPath = "models/asr/example/tokens.txt",
    EncoderPath = "models/asr/example/encoder.onnx",
    DecoderPath = "models/asr/example/decoder.onnx",
    JoinerPath = "models/asr/example/joiner.onnx",
    Language = "zh",
    Provider = "cpu"
});
```

## TTS 示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech;

ITextToSpeechSynthesizer tts = provider.GetRequiredService<ITextToSpeechSynthesizer>();

AudioData audio = await tts.SynthesizeAsync(
    "你好，欢迎使用 Zhengyan.DigitalWife。",
    new SpeechSynthesisOptions { ModelKind = SpeechSynthesisModelKind.Vits });
await WaveFile.WriteAsync("tts.wav", audio);
```

## Matcha 说明

`Matcha` 模型需要把 `ModelKind` 设为 `Matcha`，并额外提供 vocoder 文件路径。

如果你不显式设置：

- `VocoderPath`
- `RuleFsts`

运行时会尝试从 `ModelPath` 所在目录自动推断默认文件。

## GPU / Provider 说明

`Provider` 会传到底层 `SherpaOnnx` / ONNX Runtime。

但这不代表“只改配置就一定上 GPU”。是否真正走 GPU 还取决于：

- 你传入的 `Provider` 值
- 当前部署目录里是否存在对应的 ONNX Runtime GPU native libraries
- 服务器上的 GPU / CUDA 环境是否匹配

在 `RealtimeVoice` 服务里，启动日志会额外打印：

- `requestedProvider`
- `cudaProviderBinaryDetected`

如果你在 NVIDIA 服务器上配置了 GPU provider，但日志仍提示没检测到 CUDA provider 二进制，那么当前部署大概率仍会回退到 CPU 或直接无法用 GPU。

## 适合什么场景

- 希望统一用一个 Provider 覆盖离线识别、唤醒词和 TTS
- 需要本地部署、低延迟、可离线运行的中文语音能力
- 需要在同一技术栈里控制 ASR、TTS 和唤醒词
