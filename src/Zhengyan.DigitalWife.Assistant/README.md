# Zhengyan.DigitalWife.Assistant

`Zhengyan.DigitalWife.Assistant` 是语音助手编排层。它基于 `Abstractions` 中的接口，把录音、识别、LLM、分句、TTS、播放串成一条完整链路。

## 命名空间

- `Zhengyan.DigitalWife.Assistant`
- `Zhengyan.DigitalWife.Assistant.Conversation`
- `Zhengyan.DigitalWife.Assistant.Text`

## 主要 API

### `ServiceCollectionExtensions`

- `AddDigitalWifeAssistantCore(IServiceCollection services)`

注册：

- `SentenceChunker`
- `VoiceAssistantPipeline`

### `VoiceAssistantPipeline`

核心方法：

- `Task<VoiceAssistantTurnResult> RunTurnAsync(VoiceAssistantTurnOptions options, CancellationToken cancellationToken = default)`

执行流程：

1. 调用 `IAudioSource.RecordUntilSilenceAsync()` 录音
2. 用已注册的 `ISpeechRecognizer` 列表按顺序识别，空结果时自动回退
3. 调用 `ILlmClient.StreamChatAsync()` 获取流式回答
4. 用 `SentenceChunker` 把增量文本切成句子
5. 对每句调用 `ITextToSpeechSynthesizer`
6. 调用 `IAudioPlayer.PlayAsync()` 逐句播放

### `VoiceAssistantTurnOptions`

常用字段：

- `SystemPrompt`
- `History`
- `LlmOptions`
- `CaptureOptions`
- `RecognitionOptions`
- `SynthesisOptions`
- `CapturedAudioPath`

### `VoiceAssistantTurnResult`

- `UserText`
- `AssistantText`
- `SpokenSentences`

### `SentenceChunker`

用于把 LLM 的流式输出按句子边界切块，适合“边生成、边合成、边播放”的场景。

## DI 组合示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Assistant;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;

ServiceCollection services = new();

services.AddDigitalWifeAssistantCore();
services.AddPortAudio();
services.AddOpenAiCompatibleLlmClient(new OpenAiCompatibleLlmOptions
{
    BaseUrl = "http://127.0.0.1:8000",
    ApiKey = "YOUR_KEY"
});
services.AddSherpaOnnxSpeechRecognizer(new SherpaOnnxRecognizerOptions
{
    ModelKind = SherpaOnnxRecognizerModelKind.OnlineTransducer,
    TokensPath = "models/asr/example/tokens.txt",
    EncoderPath = "models/asr/example/encoder.onnx",
    DecoderPath = "models/asr/example/decoder.onnx",
    JoinerPath = "models/asr/example/joiner.onnx"
});
services.AddSherpaOnnxTextToSpeech(new SherpaOnnxTtsOptions
{
    ModelPath = "models/tts/example/model.onnx",
    TokensPath = "models/tts/example/tokens.txt"
});
```

## 运行单轮语音助手

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Assistant.Conversation;
using Zhengyan.DigitalWife.Llm;
using Zhengyan.DigitalWife.Speech;

ServiceProvider provider = services.BuildServiceProvider();
VoiceAssistantPipeline pipeline = provider.GetRequiredService<VoiceAssistantPipeline>();

VoiceAssistantTurnResult result = await pipeline.RunTurnAsync(new VoiceAssistantTurnOptions
{
    SystemPrompt = "你是一个中文语音助手，请用简洁自然的语气回答。",
    LlmOptions = new LlmRequestOptions
    {
        Model = "qwen2.5-14b-instruct"
    },
    CaptureOptions = new VoiceActivityCaptureOptions
    {
        SampleRate = 16000,
        Channels = 1
    },
    RecognitionOptions = new SpeechRecognitionOptions
    {
        Language = "zh",
        EnableTimestamps = true
    },
    SynthesisOptions = new SpeechSynthesisOptions
    {
        ModelKind = SpeechSynthesisModelKind.Vits,
        Speed = 1.0f
    },
    CapturedAudioPath = "artifacts/captured/turn.wav"
});

Console.WriteLine(result.UserText);
Console.WriteLine(result.AssistantText);
```

## 适合什么场景

- 你要快速拼出完整“录音 -> 识别 -> LLM -> TTS -> 播放”链路。
- 你希望识别器支持回退策略。
- 你希望 TTS 以句子为粒度边生成边播。

如果你只想依赖接口层，请参考 [Zhengyan.DigitalWife.Abstractions](../Zhengyan.DigitalWife.Abstractions/README.md)。
