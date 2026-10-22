# Speech.SherpaOnnx：本地 ASR、TTS 与唤醒词

这个库使用 SherpaOnnx 提供三种语音实现：语音转文字 ISpeechRecognizer、文字转语音 ITextToSpeechSynthesizer、唤醒词 IWakeWordDetector。

三种能力需要不同模型和配置。下载一个 ASR 模型不会同时得到 TTS 声音或唤醒词模型。

## 先准备权重

在仓库根目录：

```powershell
./scripts/download-models.ps1 -SkipWhisper
```

下载到 models/asr、models/tts、models/wake。模型目录里的 encoder、decoder、joiner、tokens、词典等应与所选模型类型匹配。完整步骤见 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md)。

本库用于 PC / 服务端。Android 本地 ASR/TTS 当前走系统服务，不直接把 PC 模型配置映射为 Android 推理。

## 三组入口

| 能力 | 参数类 | 注册方法 |
| --- | --- | --- |
| ASR | SherpaOnnxRecognizerOptions | AddSherpaOnnxSpeechRecognizer |
| TTS | SherpaOnnxTtsOptions | AddSherpaOnnxTextToSpeech |
| 唤醒词 | SherpaOnnxWakeWordOptions | AddSherpaOnnxWakeWordDetector |

ASR 支持多种离线和在线模型类型。“在线模型”表示可逐段处理音频，不一定表示依赖互联网；模型文件仍由本地提供。

## 注册 ASR 示例

以下文件名是占位示例，需替换成自己的模型实际路径：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;

var services = new ServiceCollection();
services.AddSherpaOnnxSpeechRecognizer(new SherpaOnnxRecognizerOptions
{
    ModelKind = SherpaOnnxRecognizerModelKind.OnlineTransducer,
    TokensPath = "models/asr/my-model/tokens.txt",
    EncoderPath = "models/asr/my-model/encoder.onnx",
    DecoderPath = "models/asr/my-model/decoder.onnx",
    JoinerPath = "models/asr/my-model/joiner.onnx",
    Language = "zh",
    Provider = "cpu"
});
```

注册后获取 ISpeechRecognizer 处理 AudioData / WAV。录音由 PortAudio 等 IAudioSource 负责。

## TTS 与唤醒词

TTS 配置要选对 SpeechSynthesisModelKind，例如 Vits 或 Matcha。Matcha 还需要 vocoder；词表、词典、规则文件和模型应属于同一套资源，不要只复制一个 ONNX 文件。

唤醒词检测器需要关键词与专用模型。它和 GamePlayer 基于识别文本过滤的 StartWakeWordMonitoring 不是完全相同的实现，捕获窗口与行为由上层决定。

## 性能和排查

Provider 会传给底层，但 GPU 能否使用还取决于 native runtime 和部署环境。先选 cpu 检查文件、语言、输出，再考虑加速。

最常见错误是文件路径错、模型类型不匹配、词典缺失或原生库无法加载。语音服务器预热日志会显示请求的 provider 与识别/合成诊断，不能仅凭配置值宣称已上 GPU。

```text
dotnet build src/Zhengyan.DigitalWife.Speech.SherpaOnnx
```

- [项目总览](../../README.md)
- [语音服务](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
- [公共语音接口](../Zhengyan.DigitalWife.Abstractions/README.md)
