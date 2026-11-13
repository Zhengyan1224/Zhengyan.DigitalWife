# 浏览器本地语音与在线语言服务

Web 播放器与 PC、Android 使用相同的服务配置：TTS、ASR 使用游戏项目中的本地 ONNX 模型，LLM 使用 `llm.baseUrl`、`model`、`apiKey` 和 `chatCompletionsPath` 配置的在线 API。浏览器不包含离线 LLM 推理和模型选择入口。

首次加载和场景切换都会显示场景配置的加载背景、进度条与当前任务。启用的本地 TTS、ASR 在加载阶段完成初始化和预热，然后显示游戏画面；相同项目的模型在场景之间复用。ASR 预热使用静音样本，不申请麦克风权限。场景脚本开始录音或监控唤醒词时，浏览器才申请麦克风权限。

TTS 使用项目的模型、声码器、tokens 和 lexicon，支持 Matcha/VITS。ASR 使用项目的 Sherpa 模型，支持 OnlineTransducer、OnlineParaformer、OnlineZipformer2CTC。此 TTS 构建使用 lexicon，不包含 espeak 数据运行时。

Realtime Voice 的本地语音链路组合本地 ASR、在线 LLM API 和本地 TTS，支持唤醒、转录、响应、朗读、取消和对话重置。

使用 `scripts/run-web-player.ps1` 启动本地预览时，服务器提供固定的同源 LLM 入口，仅转发默认 Web 游戏配置的聊天 API，并逐块传输 SSE 响应。这样可以使用不支持浏览器跨域预检的现有 PC/Android API。部署到其他静态服务器时，需要在 API 服务器启用 CORS，或提供相同的同源转发入口。

“保存离线游戏”缓存播放器、场景、模型和引擎资源，允许断网重新打开游戏。LLM 对话仍需要网络连接。Python 由本地 Pyodide 执行，浏览器存档保存在 IndexedDB 中。

浏览器仍负责麦克风权限和 WebAudio 输出。运行时与许可证见 [OFFLINE-RUNTIMES.md](../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/vendor/OFFLINE-RUNTIMES.md)。

语音会话切换会取消旧会话尚未返回的识别结果和最终转录，防止旧回调停止新麦克风；等待浏览器音频恢复期间取消录音，也会立即释放已取得的麦克风。唤醒监听遇到静音端点时只重置识别流，继续采集音频。

回归验证：`node --test tests/web-offline.test.mjs` 检查模型预热、在线语言流和麦克风生命周期。Windows 上，在 `artifacts/web-player/wwwroot/web-game` 放置 DemoGame01 的最新 Web 导出并发布播放器后，运行 `node tests/web-loading-speech-browser.mjs`，会使用 Edge、真实本地模型和模拟麦克风验证加载界面、scene2 在线回复及朗读、scene3 连续 60 秒采集；此浏览器测试会向项目配置的 LLM API 发送一次简短请求。
