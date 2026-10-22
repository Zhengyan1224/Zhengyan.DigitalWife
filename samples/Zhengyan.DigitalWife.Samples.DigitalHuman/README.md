# DigitalHuman：连接语音服务的 3D 角色前端

这个桌面示例显示 MMD 角色、动作、气泡和口型，并在本地录音、播放，通过远端 RealtimeVoice 完成识别、LLM 和语音合成。

它是一个独立前端示例，不读取 GameEditor 游戏工程或 .dwgame。想让可编辑游戏角色对话，请用 GameEditor / GamePlayer 的 Scene.RealtimeVoice。

## 谁负责什么

```text
DigitalHuman：录音 → 发往服务 → 显示文本、播放回复、驱动口型
RealtimeVoice：ASR → LLM → TTS → 返回音频和文本
```

唤醒阶段也是先发送短录音转写，再在前端匹配 WakeWords。固定提示语调用 /v1/audio/speech，不经过 LLM 改写。

## 1. 先启动语音服务

按 [RealtimeVoice README](../Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md) 下载模型并填写 LLM，然后显式启动在 5058：

```powershell
$env:ASPNETCORE_URLS = "http://127.0.0.1:5058"
dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice
```

先确认服务启动和预热完成，再启动前端。仅打开前端不会自动启动语音后端。

## 2. 配置前端

仓库根目录：

```powershell
Copy-Item samples/Zhengyan.DigitalWife.Samples.DigitalHuman/appsettings.Local.example.json samples/Zhengyan.DigitalWife.Samples.DigitalHuman/appsettings.Local.json
```

保留已有 Local 配置，修改服务端口与密钥：

```json
{
  "DigitalHuman": {
    "Audio": {
      "PlaybackBackend": "OpenAL",
      "InputDeviceIndex": null,
      "OutputDeviceIndex": null
    },
    "Realtime": {
      "BaseUrl": "http://127.0.0.1:5058",
      "ApiKey": "",
      "Model": "zhengyan-realtime-voice"
    }
  }
}
```

示例文件里的端口可能与自己选择的服务端口不同，必须对齐。连接另一台电脑时换为服务端 IP。读取 appsettings.json 后以 Local 配置覆盖；语音模型与 LLM 配置应放在服务端。

## 3. 运行

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.DigitalHuman
```

需要 .NET 10 SDK、图形驱动、麦克风和扬声器。角色资源由构建复制。预期出现角色场景，唤醒后显示转写与回复，并播放声音和口型。

## 调整哪些参数

| 区域 | 内容 |
| --- | --- |
| Audio | 本地录音设备、播放后端 |
| Realtime | 服务根地址、路径、密钥、会话参数 |
| Conversation.WakeWords | 唤醒词 |
| Conversation.WakeWordCapture / UserCapture | 本地录音采样率和静音阈值 |
| Conversation.PostResponseIdleTimeout | 回复后继续等待时长 |
| Conversation.ReturnToStandTimeout | 长时间无输入回待机 |
| Character | 模型、服装、动作与气泡 |
| Scene | 相机、灯光、场景模型与 BGM |

麦克风始终用 PortAudio；PlaybackBackend=OpenAL 只改变说话输出。PortAudio 的 OutputDeviceIndex 仅在 PortAudio 播放时适用。

## 麦克风与口型排查

先检查系统录音，再用 AssistantConsole 查看设备：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole -- --list-devices
```

把实际输入索引填到 DigitalHuman.Audio.InputDeviceIndex。声卡采样率不支持时改 WakeWordCapture / UserCapture 的采样率，必要时试 44100 / 48000；Realtime.InputAudioSampleRate 是协议目标格式，不是本地硬件必须支持的值。

前端会按播放器预计输出延迟补偿口型起始。实际同步仍取决于模型 Mouth Morph、音频设备与服务速度。

Linux 硬件排查见[录音记录](../../docs/linux-mint-lenovo-e41-80-microphone.md)。出现服务异常时分别查看前端和后端日志。

## 从旧版配置迁移

旧 DigitalHuman 的 Llm、SherpaRecognizer、WhisperRecognizer、Tts 移到 RealtimeVoice 服务；前端保留 Realtime 地址与本地录音、角色配置。

- 原 SystemPrompt → DigitalHuman.Realtime.Instructions 或服务 Session.Instructions。
- 原 LlmModel → RealtimeVoice.Llm.Model。
- 原模型路径 → RealtimeVoice 的识别与 TTS 配置。

- [项目总览](../../README.md)
- [语音服务](../Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
- [可编辑游戏方案](../../src/Zhengyan.DigitalWife.GameEditor/README.md)
