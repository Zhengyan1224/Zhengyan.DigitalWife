---
id: overview
title: 总览
category: 基础
objects:
  - CSharpScriptGlobals
  - Python event functions
keywords:
  - overview
  - script
  - csx
  - python
---

# 总览

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 总览 |
| 分类 | 基础 |
| 主要对象 | ``CSharpScriptGlobals``, ``Python event functions`` |
| C# 入口 | `Entity, Scene, Input, Audio` |
| Python 入口 | `start/update/gui_event/...` |
| 说明 | 脚本层定位、运行时范围、资源路径建议和文档阅读入口。 |

## API 内容

本文档说明 `GamePlayer` 暴露给 C# `.csx` 和 Python `.py` 脚本的运行时 API。编辑器中绑定脚本后，保存项目时会把外部脚本复制到游戏工程目录下，并做一次轻量语法检查。

脚本层定位：

- 脚本负责游戏逻辑、输入响应、对象移动、GUI 事件、场景切换、音频播放、TTS 说话、相机控制、存档读写等。
- 脚本不直接管理 OpenGL 资源、PMX 内部骨骼求解、音频设备、窗口消息循环或底层物理引擎。
- 当前碰撞是轻量级运行时 Collider，不是完整刚体物理系统。它适合射线拾取、触发区域和简单碰撞判断。
- 网络通信通过 `Scene.Network` / `scene.network` 提供，支持 HTTP/HTTPS、TCP 和 UDP。跨平台使用 .NET / Python 标准网络库，适用于 Windows、Linux 和 macOS。
- 远端语音服务通过 `Scene.RealtimeVoice` / `scene.realtime_voice` 提供，可调用 `Zhengyan.DigitalWife.Samples.RealtimeVoice` 做转写、唤醒词监听、流式语音回复和文本直出 TTS。
- 路径建议使用工程相对路径，例如 `assets/audio/bgm.ogg`、`assets/motions/idle.vmd`、`scripts/main.csx`。GamePlayer 会从游戏工程目录解析这些路径。
- 贴图路径可以使用普通工程相对路径，也可以使用 `rt:<RenderTextureName>` 引用 Render Texture。
