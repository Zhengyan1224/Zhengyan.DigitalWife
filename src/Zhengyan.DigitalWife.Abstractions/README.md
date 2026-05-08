# Zhengyan.DigitalWife.Abstractions

`Zhengyan.DigitalWife.Abstractions` 定义整个语音子系统的公共契约。它不包含任何具体模型推理、网络调用或设备驱动代码，职责是把业务层与具体 Provider 解耦。

## 命名空间

- `Zhengyan.DigitalWife.Audio`
- `Zhengyan.DigitalWife.Speech`
- `Zhengyan.DigitalWife.Llm`

## 主要 API

### 音频

- `AudioEncoding`
  音频编码类型，当前内置 `Float32` 和 `Pcm16`。
- `AudioFormat`
  采样率、声道数、编码格式。
- `AudioChunk`
  流式音频片段，包含 `Samples`、`Format`、`Offset`、`IsFinal`。
- `AudioData`
  整段音频对象，提供：
  - `ToMono()`
  - `Resample(int targetSampleRate)`
  - `ToChunks(int chunkSampleCount = 4096, ...)`
- `AudioCaptureOptions`
  基础采集参数。
- `VoiceActivityCaptureOptions`
  带静音检测的采集参数。
- `WaveFile`
  `ReadAsync()` / `WriteAsync()` WAV 辅助方法。
- `IAudioSource`
  音频采集接口。
- `IAudioPlayer`
  音频播放接口。

### 语音识别 / 合成 / 唤醒词

- `SpeechRecognitionOptions`
- `StreamingSpeechRecognitionOptions`
- `SpeechRecognitionSegment`
- `SpeechRecognitionResult`
- `SpeechRecognitionUpdate`
- `IStreamingSpeechRecognitionSession`
- `ISpeechRecognizer`
- `SpeechSynthesisOptions`
  支持 `ModelKind` 作为单次调用的 TTS 模型类型覆盖项。
- `ITextToSpeechSynthesizer`
- `WakeWordDetectedEventArgs`
- `IWakeWordDetector`

### LLM

- `LlmChatMessage`
- `LlmRequestOptions`
- `LlmStreamUpdate`
- `ILlmClient`

## 典型用法

### 1. 构造音频数据并切片

```csharp
using Zhengyan.DigitalWife.Audio;

float[] samples = new float[16000];
AudioFormat format = new(sampleRate: 16000, channels: 1);
AudioData audio = new(samples, format);

AudioData mono = audio.ToMono();
AudioData resampled = mono.Resample(16000);

await foreach (AudioChunk chunk in resampled.ToChunks(4096))
{
    Console.WriteLine($"{chunk.Offset} / {chunk.Duration} / final={chunk.IsFinal}");
}
```

### 2. 以接口方式消费识别器与 LLM

```csharp
using System.Text;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Speech;

static async Task<string> DescribeAudioAsync(
    ISpeechRecognizer recognizer,
    ILlmClient llm,
    AudioData audio,
    CancellationToken cancellationToken = default)
{
    SpeechRecognitionResult recognition = await recognizer.RecognizeAsync(
        audio,
        new SpeechRecognitionOptions
        {
            Language = "zh",
            EnableTimestamps = true
        },
        cancellationToken);

    List<LlmChatMessage> messages =
    [
        new("system", "请把用户的话整理成一句简洁摘要。"),
        new("user", recognition.Text)
    ];

    StringBuilder builder = new();
    await foreach (LlmStreamUpdate update in llm.StreamChatAsync(
        messages,
        new LlmRequestOptions { Model = "qwen2.5-14b-instruct" },
        cancellationToken))
    {
        builder.Append(update.Delta);
    }

    return builder.ToString();
}
```

### 3. 使用 `WaveFile` 保存录音结果

```csharp
using Zhengyan.DigitalWife.Audio;

AudioData audio = new(new float[32000], new AudioFormat(16000, 1));
await WaveFile.WriteAsync("captured.wav", audio);

AudioData restored = await WaveFile.ReadAsync("captured.wav");
Console.WriteLine(restored.Duration);
```

## 适用场景

- 你只想依赖接口，不想绑定具体 Provider。
- 你希望把 `SherpaOnnx`、`Whisper.net`、OpenAI 兼容接口等实现做成可替换模块。
- 你要自己写新的音频、语音、LLM Provider。

如果你还需要现成的语音助手编排，请继续参考 [Zhengyan.DigitalWife.Assistant](../Zhengyan.DigitalWife.Assistant/README.md)。
