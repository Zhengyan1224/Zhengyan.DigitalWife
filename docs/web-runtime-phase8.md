# Web Runtime 阶段 8：语音、网络和生产发布【已实现】

> 状态：**已实现**

已实现浏览器平台服务适配层：

- `WebSpeechService`：SpeechSynthesis TTS；
- `WebMicrophoneService`：麦克风权限、MediaRecorder 录音和停止；
- `WebNetworkService`：受控 JSON HTTP、WebSocket 连接/发送/关闭；
- `web-platform.js`：浏览器能力检查和统一异常转换；
- `sw.js`：生产静态资源缓存、版本清理和离线优先读取；
- WebRuntime 启动时注册 Service Worker；
- API 密钥仍由服务端代理或短期授权提供，公开 Web 包不硬编码长期密钥。

本阶段不把本地 ASR/TTS 模型塞入 Web 包；远程 ASR、TTS、LLM 和实时语音服务通过 `WebNetworkService` 接入。
