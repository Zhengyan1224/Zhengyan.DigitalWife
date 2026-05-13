# Zhengyan.DigitalWife.Abstractions

`Zhengyan.DigitalWife.Abstractions` 定义整个语音子系统的公共契约。

它不包含具体模型推理、网络调用或设备驱动实现，职责是把业务层与具体 Provider 解耦。

## 命名空间

- `Zhengyan.DigitalWife.Audio`
- `Zhengyan.DigitalWife.Speech`
- `Zhengyan.DigitalWife.Llm`

## 主要 API

### 音频

- `AudioEncoding`
- `AudioFormat`
- `AudioChunk`
- `AudioData`
- `AudioCaptureOptions`
- `VoiceActivityCaptureOptions`
- `WaveFile`
- `IAudioSource`
- `IAudioPlayer`
- `IAudioPlaybackTiming`

### 语音识别 / 合成 / 唤醒词

- `SpeechRecognitionOptions`
- `StreamingSpeechRecognitionOptions`
- `SpeechRecognitionSegment`
- `SpeechRecognitionResult`
- `SpeechRecognitionUpdate`
- `IStreamingSpeechRecognitionSession`
- `ISpeechRecognizer`
- `SpeechSynthesisOptions`
- `ITextToSpeechSynthesizer`
- `WakeWordDetectedEventArgs`
- `IWakeWordDetector`

### LLM

- `LlmChatMessage`
- `LlmRequestOptions`
- `LlmStreamUpdate`
- `ILlmClient`

## `WaveFile`

`WaveFile` 当前支持：

- `ReadAsync(string path, ...)`
- `ReadAsync(Stream stream, ...)`
- `WriteAsync(string path, ...)`
- `WriteAsync(Stream stream, ...)`

这意味着：

- 可以直接读写本地 WAV 文件
- 也可以直接处理 HTTP 响应流、内存流等非文件流
- 对不可 seek 的流，内部会先做缓冲再解析

## `IAudioPlaybackTiming`

`IAudioPlaybackTiming` 用来让播放器暴露“预计实际出声延迟”，典型用途是：

- 让口型启动更贴近真实播出时刻
- 减少“嘴先动，声音后到”的观感

当前 `PortAudio` 播放器已经实现这个接口。

## 典型用法

### 构造音频并切片

```csharp
using Zhengyan.DigitalWife.Audio;

float[] samples = new float[16000];
AudioFormat format = new(sampleRate: 16000, channels: 1);
AudioData audio = new(samples, format);

await foreach (AudioChunk chunk in audio.ToChunks(4096))
{
    Console.WriteLine($"{chunk.Offset} / {chunk.Duration} / final={chunk.IsFinal}");
}
```

### 以接口方式消费识别器与 LLM

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

## 适用场景

- 你只想依赖接口，不想绑定具体 Provider
- 你希望把 `SherpaOnnx`、`Whisper.net`、OpenAI 兼容接口等实现做成可替换模块
- 你要自己写新的音频、语音、LLM Provider

如果你还需要现成的语音助手编排，请继续参考 [Zhengyan.DigitalWife.Assistant](../Zhengyan.DigitalWife.Assistant/README.md)。
