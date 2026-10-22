# AssistantConsole：先跑通语音助手

这个控制台示例演示“录音 → 本地 ASR → LLM 文本回复 → 本地 TTS → 播放”。不显示 3D 角色，适合先把语音设备、模型与服务分别验证。

想体验带角色的远端语音方案，请看 [DigitalHuman](../Zhengyan.DigitalWife.Samples.DigitalHuman/README.md)。

## 需要什么

- .NET 10 SDK。
- 可用麦克风和扬声器。
- ASR / TTS 模型，持续唤醒还需要唤醒词模型。
- 已运行的 LLM 服务及正确模型名、鉴权信息。
- Linux 原生音频依赖，见[主 README](../../README.md#linux-运行依赖)。

仅列设备不需要推理模型；完整对话必须先配置。

## 1. 下载模型

仓库根目录：

```powershell
./scripts/download-models.ps1
```

Linux / macOS：

```bash
bash scripts/download-models.sh
```

模型下载到 models，不会启动 LLM。先检查 appsettings.json 中的模型路径与下载目录一致。

## 2. 复制并填写本地配置

```powershell
Copy-Item samples/Zhengyan.DigitalWife.Samples.AssistantConsole/appsettings.Local.example.json samples/Zhengyan.DigitalWife.Samples.AssistantConsole/appsettings.Local.json
```

如果已有 appsettings.Local.json，请保留自己的值。Linux / macOS 使用 cp。Local 文件用于本机覆盖，不提交到 Git。

本示例的模型名使用 **Demo.LlmModel**，不是只设置 Demo.Llm.Model：

```json
{
  "Demo": {
    "LlmModel": "YOUR_MODEL",
    "Llm": {
      "BaseUrl": "http://127.0.0.1:8000",
      "ApiKey": "YOUR_KEY",
      "ChatCompletionsPath": "/v1/chat/completions"
    }
  }
}
```

替换地址、模型和密钥。RecognitionProvider 选择 sherpa / whisper；Capture 是本地麦克风参数，Audio.PlaybackBackend 选择 PortAudio / OpenAL。OpenAL 只影响播放，录音仍走 PortAudio。

配置读取 appsettings.json，再读取可选 appsettings.Local.json；本示例还支持 DIGITALWIFE_ / SPEECHBRIDGE_ 环境变量前缀。

## 3. 先列出设备

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --list-devices
```

找出输入设备名称、索引与默认采样率。PortAudio 索引不是 ALSA 声卡号，也不适用于 Android。

## 4. 运行一轮

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --run-once
```

说一句话并停顿，预期控制台出现用户与回复文本，扬声器播放回复。指定设备：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --run-once --input-device 1 --output-device 2
```

数字是示例，要换为设备枚举结果。--run-once 跳过持续唤醒；直接运行则在配置了 WakeWord 时等待唤醒词，否则执行一轮。

## 其它参数

| 参数 | 作用 |
| --- | --- |
| --list-devices | 列出设备后退出 |
| --run-once | 直接执行一轮 |
| --input-device / --output-device | 覆盖 PortAudio 设备 |
| --transcribe-file | 转写 WAV 文件后退出 |

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --transcribe-file "D:/Audio/question.wav"
```

转写模式只需要所选 ASR，仍要有可解析的配置；不会请求完整对话回复。

## 排查顺序

1. 系统录音正常吗？先验证硬件与权限。
2. 枚举索引正确吗？不要照搬另一台电脑的索引。
3. 采样率被设备支持吗？必要时改为设备默认值或 44100 / 48000。
4. ASR 返回文本吗？先用 WAV 单独转写。
5. LLM 服务地址、模型名、密钥正确吗？
6. TTS 文件与播放设备正常吗？

设备忙先关闭占用程序。特定 Linux 内置麦克风案例见[排查记录](../../docs/linux-mint-lenovo-e41-80-microphone.md)，不要把硬件特定驱动参数直接应用到所有机器。

- [项目总览](../../README.md)
- [Assistant 编排层](../../src/Zhengyan.DigitalWife.Assistant/README.md)
- [PortAudio](../../src/Zhengyan.DigitalWife.Audio.PortAudio/README.md)
