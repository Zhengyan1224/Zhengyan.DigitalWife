# GameEditor / GamePlayer 脚本 API

本文件现在是轻量索引。完整 API 文档已按模块拆分到 [`api_docs`](./api_docs/) 目录，避免单个 Markdown 文件过大导致编辑器卡顿。

建议优先打开 [`script_api.html`](./script_api.html) 使用检索页面；需要直接阅读 Markdown 时，从下表进入对应模块。

## 模块索引

| 模块 | 分类 | 主要对象 / 入口 | 文档 |
| --- | --- | --- | --- |
| 总览 | 基础 | ``CSharpScriptGlobals``, ``Python event functions`` | [api_docs/00-overview.md](./api_docs/00-overview.md) |
| 路径规则 | 基础 | ``GameProjectPath`` | [api_docs/01-paths.md](./api_docs/01-paths.md) |
| 脚本类型 | 基础 | ``C# .csx``, ``Python .py`` | [api_docs/02-script-types.md](./api_docs/02-script-types.md) |
| 基础系统 API | 基础 | ``System``, ``Python stdlib`` | [api_docs/03-system-api.md](./api_docs/03-system-api.md) |
| 生命周期 | 事件 | ``CSharpScriptGlobals``, ``Python event functions`` | [api_docs/04-lifecycle.md](./api_docs/04-lifecycle.md) |
| Entity API | 对象 | ``RuntimeEntity``, ``entity`` | [api_docs/05-entity.md](./api_docs/05-entity.md) |
| PMX 动作与材质 | 对象 | ``RuntimeEntity PMX``, ``PmxNodeState`` | [api_docs/06-pmx.md](./api_docs/06-pmx.md) |
| 水面与粒子触水 | 对象 | ``RuntimeEntity water_surface``, ``RuntimeEntity particle_system`` | [api_docs/07-water-particles.md](./api_docs/07-water-particles.md) |
| TTS / 人物说话 | 音频 | ``RuntimeEntity``, ``RuntimeVoiceOptions`` | [api_docs/08-tts.md](./api_docs/08-tts.md) |
| Input 输入 | 输入 | ``RuntimeInput``, ``input`` | [api_docs/09-input.md](./api_docs/09-input.md) |
| Audio 音频 / 背景音乐 | 音频 | ``RuntimeAudio``, ``audio`` | [api_docs/10-audio.md](./api_docs/10-audio.md) |
| Scene API | 对象 | ``RuntimeScene``, ``scene`` | [api_docs/11-scene.md](./api_docs/11-scene.md) |
| Performance / FPS API | 调试 | ``RuntimePerformance``, ``scene.performance`` | [api_docs/12-performance.md](./api_docs/12-performance.md) |
| 场景加载入口脚本 | 事件 | ``Loading scripts`` | [api_docs/13-loading-scripts.md](./api_docs/13-loading-scripts.md) |
| Dialogue Bubble API | GUI | ``RuntimeDialogueBubbleManager``, ``RuntimeDialogueBubble``, ``scene.bubble`` | [api_docs/14-dialogue-bubble.md](./api_docs/14-dialogue-bubble.md) |
| GUI API | GUI | ``RuntimeGuiControl``, ``GuiControlSettings`` | [api_docs/15-gui.md](./api_docs/15-gui.md) |
| 2D Sprite API | GUI | ``RuntimeSpriteControl``, ``SpriteSettings`` | [api_docs/16-sprite.md](./api_docs/16-sprite.md) |
| Window / Runtime 设置 | 运行时 | ``RuntimeWindowControl``, ``RuntimeProjectControl``, ``RuntimeCommandResult`` | [api_docs/17-window-runtime.md](./api_docs/17-window-runtime.md) |
| Camera API | 相机 | ``RuntimeCamera``, ``RuntimeRay`` | [api_docs/18-camera.md](./api_docs/18-camera.md) |
| 射线与拾取 | 相机 | ``RuntimeRay``, ``RuntimeRaycastHit`` | [api_docs/19-ray-picking.md](./api_docs/19-ray-picking.md) |
| Physics / Grounding API | 物理 | ``RuntimeScenePhysics``, ``RuntimeRaycastHit``, ``scene.physics`` | [api_docs/21-physics-grounding.md](./api_docs/21-physics-grounding.md) |
| Collision / Collider API | 物理 | ``RuntimeCollider``, ``RuntimeRaycastHit``, ``MeshCollider`` | [api_docs/20-collision.md](./api_docs/20-collision.md) |
| NavMesh API | 物理 | ``RuntimeSceneNavigation``, ``RuntimeNavigationPath`` | [api_docs/29-navmesh.md](./api_docs/29-navmesh.md) |
| 多相机与 Render Texture | 渲染 | ``RuntimeCamera``, ``RuntimeSpriteControl``, Android ``Services.GetRenderTexture`` | [api_docs/21-render-texture.md](./api_docs/21-render-texture.md) |
| Android RenderTexture / GLES Shader | 渲染 | ``Services.RefreshRenderTexture``, ``Services.ConfigureRenderTexture``, ``AndroidGlesShaderContract`` | [api_docs/30-android-render-texture.md](./api_docs/30-android-render-texture.md) |
| Save 存档 API | 存档 | ``RuntimeSaveStore``, ``scene.save`` | [api_docs/22-save.md](./api_docs/22-save.md) |
| Network 网络通信 API | 网络 | ``RuntimeNetwork``, ``RuntimeHttpResponse``, ``RuntimeTcpMessage``, ``RuntimeUdpMessage`` | [api_docs/23-network.md](./api_docs/23-network.md) |
| ASR API | 语音 | ``RuntimeAsr``, ``RuntimeAsrScriptEvent``, ``scene.asr`` | [api_docs/24-asr.md](./api_docs/24-asr.md) |
| LLM / OpenAI-compatible API | AI | ``RuntimeLlm``, ``RuntimeLlmScriptEvent``, ``RuntimeLlmChatMessage`` | [api_docs/25-llm.md](./api_docs/25-llm.md) |
| Realtime Voice API | 语音 | ``RuntimeRealtimeVoice``, ``RuntimeRealtimeVoiceScriptEvent``, ``scene.realtime_voice`` | [api_docs/26-realtime-voice.md](./api_docs/26-realtime-voice.md) |
| 常见脚本组合示例 | 示例 | ``Recipes`` | [api_docs/27-recipes.md](./api_docs/27-recipes.md) |
| 当前边界与注意事项 | 边界 | ``Limitations`` | [api_docs/28-notes.md](./api_docs/28-notes.md) |

## 结构约定

- 每个模块 Markdown 都包含 `结构化索引`，列出分类、主要对象、C# 入口、Python 入口和用途。
- 具体属性、方法、参数和示例保留在模块正文中，表格和代码块会被 `script_api.html` 渲染为可检索内容。
- `api_docs/api_manifest.json` 提供轻量元数据；`api_docs/api_manifest.js` 提供 HTML 离线检索和展示所需的数据快照。

## 更新提示

如果后续修改了某个模块文档，需要运行 `python api_docs/build_manifest.py` 同步更新 `api_docs/api_manifest.json` 和 `api_docs/api_manifest.js`，否则 `script_api.html` 的离线快照不会包含最新内容。
