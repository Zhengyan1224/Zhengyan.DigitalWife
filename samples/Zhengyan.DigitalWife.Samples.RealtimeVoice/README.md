# RealtimeVoice：把语音推理放到服务端

这是可独立运行的语音后端示例，向 GamePlayer、DigitalHuman 等客户端提供“ASR → LLM → TTS”。前端只负责本地录音、显示和播放，较重的语音模型可放在电脑或服务器。

它不操作服务器麦克风和扬声器，也不是自动部署 LLM 的工具。LLM 仍由另一个兼容服务提供。

## 先准备什么

- .NET 10 SDK；发布后运行所需 Runtime 取决于发布方式。
- 本地可读的 Sherpa / Whisper ASR 与 Sherpa TTS 模型。
- 已可访问的 Chat Completions 服务。
- 客户端与服务端能互相连接。

先用 CPU 配置跑通，再考虑 GPU。只是启动 3D 编辑器不需要运行本服务。

## 1. 下载语音模型

在仓库根目录：

```powershell
./scripts/download-models.ps1
```

Linux / macOS 用 bash scripts/download-models.sh。下载文件不等于启动服务，检查 appsettings.json 中各模型路径与实际目录匹配。

## 2. 创建本地覆盖配置

```powershell
Copy-Item samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/appsettings.Local.example.json samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/appsettings.Local.json
```

已有 Local 配置请保留。填写示例中的占位值：

```json
{
  "RealtimeVoice": {
    "ApiKey": "",
    "Llm": {
      "BaseUrl": "http://127.0.0.1:8000",
      "ApiKey": "YOUR_LLM_KEY",
      "Model": "YOUR_MODEL",
      "ChatCompletionsPath": "/v1/chat/completions"
    }
  }
}
```

RealtimeVoice.ApiKey 是**客户端访问本语音服务**的密钥；Llm.ApiKey 是**本服务访问 LLM**的密钥，两者可以不同。上面的空服务密钥适合本机调试，部署时按访问范围设置鉴权。

本服务读取 appsettings.json 和 appsettings.Local.json，后者覆盖同名项。Model 使用 RealtimeVoice.Llm.Model，与 AssistantConsole 的 Demo.LlmModel 不同。

## 3. 启动并检查

显式选定端口 5058，避免与客户端默认端口不一致：

```powershell
$env:ASPNETCORE_URLS = "http://127.0.0.1:5058"
dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice
```

Linux / macOS：

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice
```

启动会预热已注册 ASR 和 TTS，首次加载可能耗时。检查模型与运行时日志，再访问 http://127.0.0.1:5058/healthz。健康入口不替代实际语音转写和 LLM 对话测试。

连接 [DigitalHuman](../Zhengyan.DigitalWife.Samples.DigitalHuman/README.md) 或在 GameEditor 配置 Scene.RealtimeVoice 后测试。

## 提供的接口

| 入口 | 用途 |
| --- | --- |
| GET / | 服务信息 |
| GET /healthz | 健康信息 |
| GET /v1/realtime | WebSocket 语音/文本会话 |
| POST /v1/audio/speech | 固定文本直接合成语音 |

若配置服务 ApiKey，客户端携带 Authorization: Bearer。WebSocket 处理会话更新、音频缓冲提交、会话项增删、创建/取消回复，返回转写、回复文本与音频事件。

协议覆盖当前示例和引擎使用的链路，不是 OpenAI Realtime 全量协议镜像。HTTP TTS 支持 input、model、voice、response_format、speed；输出格式为 wav 或 pcm。

## 哪些配置最常用

| 配置 | 用途 |
| --- | --- |
| Llm.* | 外部 LLM 地址、密钥、模型和超时 |
| Session.* | 对外会话模型、指令、输入/输出音频格式、voice |
| RecognitionPriority | sherpa / whisper 的尝试顺序 |
| SherpaRecognizer / WhisperRecognizer | 本地 ASR 模型 |
| Tts | 本地合成模型、词表、词典和 vocoder |
| Synthesis.Speed / SpeakerId | 默认声音与语速 |
| ResponseChunking | 流式回复分句策略 |
| HistoryMaxMessages | LLM 历史长度 |
| UseFallbackRecognizersForTranscription | 空转写时是否回退识别 |

Session.Model 是对外的会话名称，Llm.Model 是实际请求 LLM 的模型名称，不能混为一项。ASR、TTS 权重和声码器各有自己的文件布局。

## 部署到另一台机器

先发布：

```text
dotnet publish samples/Zhengyan.DigitalWife.Samples.RealtimeVoice -c Release -o artifacts/realtimevoice
```

发布目录还要能访问配置指定的模型；模型不会仅因 dotnet publish 自动出现在正确目录。启动时配置可访问的监听地址，并检查防火墙。前端填写服务器 IP，手机上的 127.0.0.1 指手机自己。

生产使用可通过支持 WebSocket 的反向代理提供 HTTPS / WSS，保护密钥。网络权限、模型容量、并发和超时需要按自己的部署环境评估。

## GPU 和排查

- Whisper UseGpu=true 只请求 GPU，检查 loadedRuntimeLibrary 是否实际为 Cuda。
- Sherpa Provider 传到底层，检查对应 native GPU runtime 与诊断。
- 模型预热失败先查路径与依赖，不要仅增加 Timeout。
- 没有转写先检查前端录音和协议采样率；本服务没有自己的录音设备。
- 回复有文本但没声音，检查 TTS 配置、voice 与前端播放。
- 连接失败先对齐端口、密钥与 WS / WSS 路径。

- [项目总览](../../README.md)
- [DigitalHuman 前端](../Zhengyan.DigitalWife.Samples.DigitalHuman/README.md)
- [引擎 RealtimeVoice API](../../src/Zhengyan.DigitalWife.GameEditor/api_docs/26-realtime-voice.md)
