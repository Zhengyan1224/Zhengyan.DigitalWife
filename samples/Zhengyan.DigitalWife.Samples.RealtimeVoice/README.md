# Zhengyan.DigitalWife.Samples.RealtimeVoice

`Zhengyan.DigitalWife.Samples.RealtimeVoice` 是可独立部署的语音后端样例。

它把 `ASR -> LLM -> TTS` 封装成 OpenAI 风格接口，供 `DigitalHuman` 或其它兼容客户端调用。当前样例同时提供：

- `GET /v1/realtime` WebSocket 对话接口
- `POST /v1/audio/speech` 文本直出 TTS 接口

## 目标

- 把高 CPU 占用的语音链路从 3D 前端进程中剥离
- 让前端切换供应商时尽量只改地址和配置
- 支持 Windows / Linux / macOS 部署

## 当前后端实现

- ASR：`SherpaOnnx` / `Whisper.net`
- LLM：OpenAI 兼容 `Chat Completions`
- TTS：`SherpaOnnx TTS`

注意：这个服务**不直接操作麦克风和扬声器**。音频输入输出设备由前端 `DigitalHuman` 本地进程负责，`RealtimeVoice` 只处理推理和协议交互。

## 启动

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/Zhengyan.DigitalWife.Samples.RealtimeVoice.csproj
```

如需显式指定监听地址：

```powershell
$env:ASPNETCORE_URLS="http://0.0.0.0:5058"
dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/Zhengyan.DigitalWife.Samples.RealtimeVoice.csproj
```

## 启动预热

服务启动时会主动预热：

- 所有已注册的 ASR provider
- 当前已注册的 TTS provider

这样第一次真实交互时不会再把模型加载延迟暴露给用户。

## 入口

- `GET /`
- `GET /healthz`
- `GET /v1/realtime`
- `POST /v1/audio/speech`

如果配置了 `RealtimeVoice.ApiKey`，客户端需要携带：

```http
Authorization: Bearer <ApiKey>
```

## `/v1/realtime`

### 已支持的客户端事件

- `session.update`
- `input_audio_buffer.append`
- `input_audio_buffer.commit`
- `input_audio_buffer.clear`
- `conversation.item.create`
- `conversation.item.delete`
- `response.create`
- `response.cancel`

### 已支持的服务端事件

- `session.created`
- `session.updated`
- `input_audio_buffer.committed`
- `input_audio_buffer.cleared`
- `conversation.item.created`
- `conversation.item.deleted`
- `conversation.item.input_audio_transcription.completed`
- `response.created`
- `response.output_item.added`
- `response.content_part.added`
- `response.output_audio.delta`
- `response.output_audio.done`
- `response.output_audio_transcript.delta`
- `response.output_audio_transcript.done`
- `response.output_text.delta`
- `response.output_text.done`
- `response.content_part.done`
- `response.output_item.done`
- `response.done`
- `error`

当前实现覆盖 `DigitalHuman` 所需完整链路，但不是 OpenAI Realtime 全量特性的逐项镜像。

## `/v1/audio/speech`

这是固定提示语、唤醒应答语、超时收尾语这类“文本直出语音”接口。

当前支持字段：

- `model`
- `input`
- `voice`
- `response_format`
- `speed`

当前支持的 `response_format`：

- `wav`
- `pcm`

## 配置文件

- `appsettings.json`
- `appsettings.Local.json`
- `appsettings.Local.example.json`

## 关键配置

### 基础信息

| 字段 | 作用 |
| --- | --- |
| `ApiKey` | 服务端 Bearer Token。留空则不鉴权。 |
| `HistoryMaxMessages` | 进入 LLM 的历史消息条数。 |
| `UseFallbackRecognizersForTranscription` | ASR 为空时是否继续尝试后续识别器。 |

### LLM

| 字段 | 作用 |
| --- | --- |
| `Llm.BaseUrl` | OpenAI 兼容 LLM 服务地址。 |
| `Llm.ApiKey` | LLM 鉴权密钥。 |
| `Llm.Model` | 实际发给 `Chat Completions` 后端的模型名。 |
| `Llm.ChatCompletionsPath` | Chat Completions 路径。 |
| `Llm.Timeout` | 单次请求超时。 |

`RealtimeVoice` 已不再使用顶层 `LlmModel`；请统一使用 `Llm.Model`。

### Realtime 会话默认值

| 字段 | 作用 |
| --- | --- |
| `Session.Model` | 对外暴露的默认 Realtime 会话模型名。 |
| `Session.Instructions` | 会话默认系统指令。 |
| `Session.OutputModalities` | 默认输出模态。通常为 `[ "audio" ]`。 |
| `Session.Voice` | 默认 voice。当前样例把它解释为说话人 ID 字符串。 |
| `Session.InputAudioSampleRate` | Realtime 输入音频协议采样率。 |
| `Session.OutputAudioSampleRate` | Realtime 输出音频协议采样率。 |
| `Session.InputTranscriptionModel` | 输入转写模型名。 |
| `Session.InputTranscriptionLanguage` | 输入转写语言。 |
| `Session.MaxOutputTokens` | 回答最大输出 token 数。 |
| `Session.Temperature` | 回答温度。 |

### ASR

| 字段 | 作用 |
| --- | --- |
| `RecognitionProvider` | 兼容旧配置的默认识别器名称。 |
| `RecognitionPriority` | 实际识别器尝试顺序。 |
| `SherpaRecognizer` | SherpaOnnx ASR 配置。 |
| `WhisperRecognizer` | Whisper.net ASR 配置。 |

### TTS

| 字段 | 作用 |
| --- | --- |
| `Tts` | SherpaOnnx TTS 模型配置。 |
| `Synthesis.Speed` | 默认语速。 |
| `Synthesis.SpeakerId` | 默认说话人 ID。 |

### 分句

| 字段 | 作用 |
| --- | --- |
| `ResponseChunking.EnableClauseBoundaries` | 是否允许按分句边界提前开始 TTS。 |
| `ResponseChunking.MinClauseCharacters` | 提前切句的最小字符数。 |
| `ResponseChunking.MaxBufferedCharacters` | 长文本强制切段阈值。 |

## GPU 说明

### Whisper.net

- `WhisperRecognizer.UseGpu = true` 会请求 GPU
- 当前仓库已经引入 `Whisper.net.Runtime.Cuda`
- 但是否真正加载到 CUDA，还要看部署机器和 native runtime 是否匹配

启动时 `RealtimeVoice` 会打印诊断日志，重点看：

- `loadedRuntimeLibrary=Cuda`

### SherpaOnnx

- `SherpaRecognizer.Provider` 会传到底层
- 但是否真的走 GPU，取决于部署目录里是否具备对应的 ONNX Runtime GPU native libraries

启动日志会额外打印：

- `requestedProvider`
- `cudaProviderBinaryDetected`

如果你在 NVIDIA 服务器上部署，建议同时看：

- `RealtimeVoice` 启动日志
- `nvidia-smi`

## 运行方式建议

### 本地开发

1. 复制 `appsettings.Local.example.json` 为 `appsettings.Local.json`
2. 填写 `RealtimeVoice.Llm.*`
3. 确认本地模型路径可访问
4. 启动 `RealtimeVoice`
5. 再启动 `DigitalHuman`

### 远端部署

推荐把这个样例单独部署到服务器：

- Windows：`dotnet run` 或发布后运行
- Linux / macOS：`dotnet publish` 后运行
- 通过反向代理暴露 `/v1/realtime` 和 `/v1/audio/speech`

## 相关文档

- [DigitalHuman README](../Zhengyan.DigitalWife.Samples.DigitalHuman/README.md)
- [OpenAI Realtime 客户端库 README](../../src/Zhengyan.DigitalWife.Realtime.OpenAI/README.md)
