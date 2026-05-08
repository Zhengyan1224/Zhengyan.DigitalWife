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

`SpeechSynthesisOptions` 也支持 `ModelKind`，可以在单次调用时覆盖配置文件中的默认模型类型。

### 唤醒词

- `SherpaOnnxWakeWordOptions`
- `SherpaOnnxWakeWordDetector`
- `AddSherpaOnnxWakeWordDetector(...)`

`SherpaOnnxWakeWordOptions` 常用字段：

- `TokensPath`
- `EncoderPath`
- `DecoderPath`
- `JoinerPath`
- `KeywordsFile`
- `SampleRate`
- `FeatureDim`
- `Threads`
- `Provider`
- `KeywordsThreshold`
- `KeywordsScore`
- `NumTrailingBlanks`
- `CaptureOptions`

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

## 识别示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Speech;

using ServiceProvider provider = services.BuildServiceProvider();
ISpeechRecognizer recognizer = provider.GetRequiredService<ISpeechRecognizer>();

AudioData audio = await WaveFile.ReadAsync("test.wav");
SpeechRecognitionResult result = await recognizer.RecognizeAsync(audio, new SpeechRecognitionOptions
{
    Language = "zh",
    EnableTimestamps = true
});

Console.WriteLine(result.Text);
```

## 注册 TTS

```csharp
services.AddSherpaOnnxTextToSpeech(new SherpaOnnxTtsOptions
{
    ModelKind = SpeechSynthesisModelKind.Vits,
    ModelPath = "models/tts/example/model.onnx",
    TokensPath = "models/tts/example/tokens.txt",
    LexiconPath = "models/tts/example/lexicon.txt",
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

### Matcha 示例

`Matcha` 模型需要把 `ModelKind` 设为 `Matcha`，并额外提供 vocoder 文件路径：
如果你不显式设置 `VocoderPath`，运行时会自动去 `ModelPath` 同目录寻找 `vocos-16khz-univ.onnx`。
如果你不显式设置 `RuleFsts`，运行时会自动在 `ModelPath` 同目录寻找 `phone-zh.fst`、`date-zh.fst` 和 `number-zh.fst`，按找到的顺序拼接。

```csharp
services.AddSherpaOnnxTextToSpeech(new SherpaOnnxTtsOptions
{
    ModelKind = SpeechSynthesisModelKind.Matcha,
    ModelPath = "models/tts/matcha-icefall-zh-en/model-steps-3.onnx",
    TokensPath = "models/tts/matcha-icefall-zh-en/tokens.txt",
    LexiconPath = "models/tts/matcha-icefall-zh-en/lexicon.txt",
    DataDirectory = "models/tts/matcha-icefall-zh-en/espeak-ng-data",
    VocoderPath = "models/tts/matcha-icefall-zh-en/vocos-16khz-univ.onnx",
    RuleFsts = "models/tts/matcha-icefall-zh-en/phone-zh.fst,models/tts/matcha-icefall-zh-en/date-zh.fst,models/tts/matcha-icefall-zh-en/number-zh.fst",
    Provider = "cpu"
});
```

## 注册唤醒词

```csharp
services.AddSherpaOnnxWakeWordDetector(new SherpaOnnxWakeWordOptions
{
    TokensPath = "models/wake/example/tokens.txt",
    EncoderPath = "models/wake/example/encoder.onnx",
    DecoderPath = "models/wake/example/decoder.onnx",
    JoinerPath = "models/wake/example/joiner.onnx",
    KeywordsFile = "models/wake/example/keywords.txt"
});
```

## 唤醒词示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech;

IWakeWordDetector detector = provider.GetRequiredService<IWakeWordDetector>();
detector.WakeWordDetected += (_, e) =>
{
    Console.WriteLine($"Wake word: {e.Keyword} @ {e.DetectedAt}");
};

await detector.StartAsync();
Console.ReadLine();
await detector.StopAsync();
await detector.DisposeAsync();
```

## 适合什么场景

- 希望统一用一个 Provider 覆盖离线识别、唤醒词和 TTS。
- 需要本地部署、低延迟、可离线运行的中文语音能力。
