# 浏览器离线运行

Web 播放器默认使用 `web.runtimeMode: "offline"`。渲染、动画、语音和模型推理均在浏览器内执行，无需本地推理服务器。

```json
"web": {
  "runtimeMode": "offline",
  "languageModelPath": "assets/models/qwen2.5-0.5b-instruct-q4_k_m.gguf",
  "languageModelContextSize": 2048,
  "languageModelThreads": 2,
  "languageModelGpuLayers": 0
}
```

语言模型通过本地 Wllama WASM 运行，沿用引擎的 LLM/SSE/工具/记忆 API。项目中的 `llm.baseUrl` 作为请求路由标识，离线模式会将该端点转交本地模型，其他外部 HTTP 请求会明确失败。可以将 GGUF 打包到项目，也可以在播放器中选择本地 GGUF 或分片；选择的模型通过 IndexedDB 按项目和模型路径保存。

TTS 使用项目现有的 ONNX 模型、声码器、tokens 和 lexicon，ASR 使用项目的 Sherpa encoder/decoder/joiner/tokens。当前附带构建支持 Matcha/VITS TTS、OnlineTransducer/OnlineParaformer/OnlineZipformer2CTC ASR。DemoGame01 的中文 Matcha/Vocos 和 Zipformer 模型已实际执行；其他模型类型不应据此标为已验证。此 TTS 构建使用 lexicon，不包含 espeak 数据运行时。

Realtime Voice 在离线模式下组合本地 ASR、LLM 和 TTS，实现转录、唤醒词、响应、朗读、取消和对话重置；不连接远程 WebSocket。浏览器仍负责麦克风权限与 WebAudio 输出。

Python 脚本由随播放器部署的 Pyodide/Python 标准库执行。支持实体/场景/GUI/输入/音频 API、Vector2/3/4、部分 Math/MathF、引擎 Task 的 await、Action 回调和存档。脚本运行目录包含项目的 `.py/.json/.txt/.md` 文件（单文件不超过 4 MiB）；`save/` 目录在每次执行后将修改合并到引擎存档，再由 IndexedDB 持久化。引擎 Save API 与普通 Python 文件写入不会覆盖彼此未修改的文件。未随项目部署的 Python 扩展包、原生 C 扩展和 CLR 模块不会自动获得支持。

用 HTTPS 或 localhost 打开静态导出后，点击“保存离线游戏”缓存默认 `web-game` 项目、模型、引擎资源和当前版本的浏览器运行时。缓存完成后可断网重新打开该地址。这个入口缓存静态导出；通过文件选择器打开的 `.dwgame` 在当前会话中直接使用内存资源，刷新后需要重新选择游戏包。浏览器存储配额与用户清理站点数据会影响缓存保留。

浏览器沙箱无法提供 PC/Android 的任意系统进程、系统命令、原生动态库或原始 TCP/UDP 套接字。离线 Python 的虚拟文件系统也不等同于主机文件系统。需要这些能力的游戏脚本必须改用浏览器接口；桌面精灵按本次需求排除。

运行时版本、源地址与许可证见 [OFFLINE-RUNTIMES.md](../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/vendor/OFFLINE-RUNTIMES.md)。相关回归包括 `tests/web-offline.test.mjs`、`tests/web-service-worker.test.mjs` 和 `WebPythonBridgeTests`。真实浏览器验证已覆盖本地中文 TTS→ASR、本地 GGUF 推理、C# LLM→Python→本地 TTS→引擎完成回调，以及断网重新加载 Python 项目和存档。ASR 模型使用合成语音样本验证，未访问真实麦克风；浏览器麦克风采集与权限仍需在设备上验证。

最终 Release AOT 发布版已通过 Python 实体/Vector/Math、项目模块 import、await、GUI 点击、虚拟文件与引擎存档共存回归。缓存 165 项运行时/测试项目资源后，关闭网络并重新加载，播放器、Pyodide、项目和存档均恢复。JavaScript 回归 65 项通过；C# WebRuntime 与模块版本/渲染检查通过。测试导出已登记 601 项资源和 16 个脚本绑定，包括本地 GGUF 模型。
