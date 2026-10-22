# Speech.WhisperNet：本地 Whisper 语音识别

这个库用 Whisper.net 实现 ISpeechRecognizer，把音频转换成文本。适合 PC / 服务端本地识别，也可以作为其它识别器的回退。

它不负责麦克风采集、TTS 或 LLM 回复。Android Scene.Asr 当前使用系统 SpeechRecognizer，不会因为包里有 Whisper 权重就自动运行这个库。

## 准备模型

需要 Whisper.cpp 格式的模型，例如下载脚本提供的 `models/whisper/ggml-base.bin`。它不是 ONNX 文件，也不是 TTS 权重。

仓库根目录：

```powershell
./scripts/download-models.ps1 -SkipAsr -SkipWake -SkipTts
```

该命令只下载 Whisper。Linux / macOS 的完整下载入口见[主 README](../../README.md#语音与-ai-如何接入)。

## 识别 WAV 文件

在自己的 .NET 10 程序中引用本库：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.WhisperNet;

var services = new ServiceCollection();
services.AddWhisperNetSpeechRecognizer(new WhisperNetRecognizerOptions
{
    ModelPath = "models/whisper/ggml-base.bin",
    Language = "zh",
    UseGpu = false,
    Threads = 4,
    SampleRate = 16000
});
using var provider = services.BuildServiceProvider();
ISpeechRecognizer recognizer = provider.GetRequiredService<ISpeechRecognizer>();
var result = await recognizer.RecognizeFileAsync("test.wav", new SpeechRecognitionOptions { Language = "zh" });
Console.WriteLine(result.Text);
```

模型路径和 test.wav 必须实际存在。首次识别还包含模型加载时间，识别语言与采样率应配合应用配置。

## 主要参数

| 参数 | 含义 |
| --- | --- |
| ModelPath | 模型权重文件 |
| Language | 语言，或 auto 自动检测 |
| TranslateToEnglish | 是否翻译到英语 |
| UseGpu | 请求 GPU runtime |
| Threads | CPU 线程数 |
| SampleRate | 识别音频采样率 |

仓库引用了 CPU 和 CUDA runtime，但 UseGpu=true 不保证 CUDA 加载成功。以诊断中的 loadedRuntimeLibrary 与运行环境为准；新手先用 CPU 跑通。

## 构建与示例

```text
dotnet build src/Zhengyan.DigitalWife.Speech.WhisperNet
```

在 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md) 中设置 RecognitionProvider，或在 [RealtimeVoice](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md) 中配置识别优先级。

- [项目总览](../../README.md)
- [Sherpa 识别 / 合成](../Zhengyan.DigitalWife.Speech.SherpaOnnx/README.md)
