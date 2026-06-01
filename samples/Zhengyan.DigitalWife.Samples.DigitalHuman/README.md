![Zhengyan.DigitalWife Logo](../../assets/mmd/samples/GameData/Logo/logo.png)

`Zhengyan.DigitalWife.Samples.DigitalHuman` 是数字人前端样例。

它负责：

- 本地麦克风采集
- 本地扬声器播放
- 本地唤醒词文本判断
- 3D 渲染、动作切换、气泡 UI、口型驱动
- 通过 OpenAI 风格协议调用远端语音服务

它不再在本地执行完整的 `ASR -> LLM -> TTS` 链路。对应的高负载语音链路已经剥离到远端 `Zhengyan.DigitalWife.Samples.RealtimeVoice`。

## 架构

当前链路如下：

1. `DigitalHuman` 本地录音
2. 唤醒词阶段把短音频片段发给远端 Realtime 服务做转写
3. 本地根据转写文本匹配 `WakeWords`
4. 正常对话阶段把整段用户语音发给远端 Realtime 服务
5. 远端完成 ASR、LLM、TTS，并按 Realtime 事件流返回文本和音频
6. `DigitalHuman` 本地播放音频，并驱动气泡和口型

固定提示语链路略有不同：

- `WakeAcknowledgementText`
- `ReturnToStandPromptText`

这两类文本现在会调用远端：

- `POST /v1/audio/speech`

也就是“文本直出 TTS”，不会再经过 LLM 改写。

## 本地音频边界

麦克风输入和扬声器播放都由 `DigitalHuman` 本地进程发起。  
`RealtimeVoice` 只处理推理与协议交互，不直接操作云端机器上的音频输入输出设备。

## 启动顺序

先启动远端语音服务：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/Zhengyan.DigitalWife.Samples.RealtimeVoice.csproj
```

再启动数字人前端：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.DigitalHuman/Zhengyan.DigitalWife.Samples.DigitalHuman.csproj
```

## 配置文件

- `appsettings.json`
- `appsettings.Local.json`
- `appsettings.Local.example.json`

通常做法是从 `appsettings.Local.example.json` 复制到 `appsettings.Local.json`，只覆盖本机相关项。

## 关键配置

### 本地音频

| 字段 | 作用 |
| --- | --- |
| `Audio.PlaybackBackend` | 说话输出后端：`PortAudio` 或 `OpenAL`。麦克风录音始终使用 `PortAudio`。 |
| `Audio.InputDeviceIndex` | 本地输入设备索引。 |
| `Audio.OutputDeviceIndex` | 本地输出设备索引。仅 `PortAudio` 播放时使用。 |
| `CapturedAudioDirectory` | 本地录音调试目录。 |
| `DeleteCapturedAudioAfterRecognition` | 转写完成后是否自动删除录音文件。 |

### Realtime 客户端

| 字段 | 作用 |
| --- | --- |
| `Realtime.BaseUrl` | 远端语音服务根地址。 |
| `Realtime.RealtimePath` | Realtime WebSocket 路径。 |
| `Realtime.AudioSpeechPath` | 文本直出 TTS 路径，默认 `/v1/audio/speech`。 |
| `Realtime.ApiKey` | 可选 Bearer Token。 |
| `Realtime.Model` | 默认会话模型名。 |
| `Realtime.Instructions` | 会话默认系统指令。 |
| `Realtime.OutputModalities` | 默认输出模态。 |
| `Realtime.Voice` | 远端 voice 字段。 |
| `Realtime.InputAudioSampleRate` | 发送到 Realtime 服务前的输入采样率。 |
| `Realtime.OutputAudioSampleRate` | 期望远端返回的输出采样率。 |
| `Realtime.InputTranscriptionModel` | 输入转写模型名。 |
| `Realtime.InputTranscriptionLanguage` | 输入转写语言。 |
| `Realtime.MaxOutputTokens` | 单次回答最大输出 token 数。 |
| `Realtime.Temperature` | 回答温度。 |
| `Realtime.Headers` | 额外请求头。 |

### 对话与唤醒

| 字段 | 作用 |
| --- | --- |
| `Conversation.WakeWords` | 本地唤醒词列表。 |
| `Conversation.WakeWordChunkDuration` | 唤醒词阶段分片录音时长。 |
| `Conversation.WakeWordExtensionDuration` | 命中前缀后的补录时长。 |
| `Conversation.WakeWordTrailingSilencePadding` | 发送转写前附加的尾部静音。 |
| `Conversation.PostResponseIdleTimeout` | 单次回答后继续等待用户输入的时长。 |
| `Conversation.ReturnToStandTimeout` | 长时间无输入后回到待机的时长。 |
| `Conversation.WakeAcknowledgementText` | 唤醒后提示语。 |
| `Conversation.ReturnToStandPromptText` | 回到待机前提示语。 |
| `Conversation.WakeWordCapture` | 唤醒词阶段本地录音参数。 |
| `Conversation.UserCapture` | 正常对话阶段本地录音参数。 |

### 角色与场景

以下部分仍然由前端本地控制：

- `Character.Body`
- `Character.Wearables`
- `Character.Actions.*`
- `Character.SpeechBubble.*`
- `Scene.Camera`
- `Scene.Lighting`
- `Scene.Models`
- `Scene.BackgroundMusic`

## 行为说明

- 唤醒词判断仍在前端本地完成，但转写由远端执行
- 正常回答语音来自远端 `/v1/realtime`
- 固定提示语来自远端 `/v1/audio/speech`
- 为了让口型和声音更同步，前端会按本地播放器预计输出延迟做口型启动补偿

## 从旧配置迁移

下面这些旧配置不再属于 `DigitalHuman`：

- `RecognitionProvider`
- `RecognitionPriority`
- `SystemPrompt`
- `LlmModel`
- `Llm.*`
- `SherpaRecognizer`
- `WhisperRecognizer`
- `Tts`

迁移关系：

| 旧位置 | 新位置 |
| --- | --- |
| `DigitalHuman.SystemPrompt` | `DigitalHuman.Realtime.Instructions` 或 `RealtimeVoice.Session.Instructions` |
| `DigitalHuman.LlmModel` | `RealtimeVoice.Llm.Model` |
| `DigitalHuman.Llm.*` | `RealtimeVoice.Llm.*` |
| `DigitalHuman.SherpaRecognizer` | `RealtimeVoice.SherpaRecognizer` |
| `DigitalHuman.WhisperRecognizer` | `RealtimeVoice.WhisperRecognizer` |
| `DigitalHuman.Tts` | `RealtimeVoice.Tts` |
| `DigitalHuman.SpeechOutput.Speed / SpeakerId` | `RealtimeVoice.Synthesis.Speed / SpeakerId` |

## 最小本地覆盖示例

```json
{
  "DigitalHuman": {
    "Audio": {
      "InputDeviceIndex": null,
      "OutputDeviceIndex": null
    },
    "Realtime": {
      "BaseUrl": "http://127.0.0.1:5058",
      "RealtimePath": "/v1/realtime",
      "AudioSpeechPath": "/v1/audio/speech",
      "ApiKey": "",
      "Model": "zhengyan-realtime-voice",
      "Instructions": "你是晓雨，一个温柔、简洁、自然的中文语音助手。请直接回答用户问题，避免冗长。"
    }
  }
}
```

## 相关文档

- [RealtimeVoice README](../Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)
- [OpenAI Realtime 客户端库 README](../../src/Zhengyan.DigitalWife.Realtime.OpenAI/README.md)
