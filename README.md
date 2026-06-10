![Zhengyan.DigitalWife Logo](assets/mmd/samples/GameData/Logo/logo.png)

# Zhengyan.DigitalWife

`Zhengyan.DigitalWife` 是一个面向跨平台数字人和轻量 3D 互动应用的 `.NET` 项目。当前核心入口已经从示例升级为：

- `Zhengyan.DigitalWife.GameEditor`：可视化编辑游戏工程、场景、角色、GUI、音频、粒子、水面、脚本、碰撞体、NavMesh、发布包等内容。
- `Zhengyan.DigitalWife.GamePlayer`：加载 GameEditor 保存的工程目录或 `.dwgame` 发布包，运行 3D 场景、脚本、语音、LLM、TTS、GUI 和桌面精灵模式。

项目仍然保留语音、Realtime 服务、MMD 基础运行时和若干 samples，但 GameEditor/GamePlayer 是后续游戏化数字人能力的主要使用入口。

## 快速开始

构建解决方案：

```powershell
dotnet build Zhengyan.DigitalWife.sln
```

启动 GameEditor：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GameEditor/Zhengyan.DigitalWife.GameEditor.csproj
```

用 GamePlayer 加载一个工程目录：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- <project-directory>
```

用 GamePlayer 加载发布包：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- D:\Games\DemoGame.dwgame
```

脚本 API 文档入口：

- Markdown 索引：[script_api.md](src/Zhengyan.DigitalWife.GameEditor/script_api.md)
- 可检索 HTML：[script_api.html](src/Zhengyan.DigitalWife.GameEditor/script_api.html)
- 分模块文档：[api_docs](src/Zhengyan.DigitalWife.GameEditor/api_docs)

## 核心项目

| 项目 | 说明 |
| --- | --- |
| [Zhengyan.DigitalWife.GameEditor](src/Zhengyan.DigitalWife.GameEditor/README.md) | 可视化工程编辑器。负责创建和维护游戏项目目录、场景 JSON、资源引用、脚本绑定、GUI、系统托盘菜单、桌面精灵设置、发布包配置等。 |
| [Zhengyan.DigitalWife.GamePlayer](src/Zhengyan.DigitalWife.GamePlayer/README.md) | 游戏运行器。负责加载工程目录或 `.dwgame` 包，运行 PMX/VMD、音频、TTS、Realtime Voice、LLM、GUI、碰撞、NavMesh、粒子、水面、镜面和脚本逻辑。 |
| [Zhengyan.DigitalWife.GameProjects](src/Zhengyan.DigitalWife.GameProjects/Zhengyan.DigitalWife.GameProjects.csproj) | GameEditor 和 GamePlayer 共享的游戏工程数据模型、资源路径、发布包格式、加密和分包逻辑。 |
| [Zhengyan.DigitalWife.Mmd.Game](src/Zhengyan.DigitalWife.Mmd.Game/README.md) | 基于 Silk.NET / OpenGL ES / OpenAL 的 3D 运行层，提供渲染、窗口、输入、音频和 MMD 场景运行基础。 |
| [Zhengyan.DigitalWife.Mmd](src/Zhengyan.DigitalWife.Mmd/README.md) | PMX / VMD 解析、骨骼、Morph、IK、物理等 MMD 基础能力。 |

## GameEditor / GamePlayer 能力

- 工程：支持普通目录形式开发调试，也支持导出 `.dwgame`、加密包和分包。
- 场景：支持多场景、主场景、场景切换、加载界面、多相机、Viewport 和 Render Texture。
- 角色：支持 PMX 模型、VMD 动作、多动作层、口型、绑定关系、TTS 说话和脚本控制。
- 脚本：支持 C# `.csx` 和 Python `.py`，可访问实体、场景、输入、音频、GUI、窗口、相机、碰撞、NavMesh、LLM、Realtime Voice 等运行时 API。
- GUI：支持按钮、标签、复选框、下拉框、文本框、进度条、对话气泡、右键菜单和脚本事件。
- 桌面精灵：支持透明窗口、点击穿透、窗口拖拽、系统托盘、托盘菜单、Windows/Linux/macOS 平台实现。
- 渲染：支持天空盒、粒子、水面交互、平面反射、Textured Plane 镜面、shadow map、自定义 shader。
- 物理和导航：支持 Box/Capsule/Mesh Collider、射线拾取、贴地采样、NavMesh 烘焙和路径查询。
- LLM：支持 OpenAI-compatible LLM、流式输出、function call、自定义工具和项目内 skills 工具。
- Realtime Voice：支持 OpenAI Realtime 风格服务接入、唤醒词、流式 ASR/LLM/TTS 和文本/气泡展示。

## 目录结构

```text
Zhengyan.DigitalWife/
├─ src/        核心源码与可执行项目
├─ samples/    示例项目
├─ tools/      仓库维护工具
├─ scripts/    辅助脚本
├─ models/     语音模型权重
├─ assets/     MMD 运行时资源与示例素材
├─ docs/       补充文档
└─ libs/       原生依赖辅助文件
```

## 语音和 AI 项目

| 项目 | 说明 |
| --- | --- |
| [Zhengyan.DigitalWife.Abstractions](src/Zhengyan.DigitalWife.Abstractions/README.md) | 音频、ASR、TTS、唤醒词、LLM 的公共接口与数据结构。 |
| [Zhengyan.DigitalWife.Assistant](src/Zhengyan.DigitalWife.Assistant/README.md) | 语音助手编排层，负责把音频输入、识别、分句和回复串成完整流水线。 |
| [Zhengyan.DigitalWife.Realtime.OpenAI](src/Zhengyan.DigitalWife.Realtime.OpenAI/README.md) | Realtime 协议模型、PCM16 编解码和客户端封装。 |
| [Zhengyan.DigitalWife.RealtimeVoice.Client](src/Zhengyan.DigitalWife.RealtimeVoice.Client/README.md) | GamePlayer/DigitalHuman 调用 Realtime Voice 服务的客户端。 |
| [Zhengyan.DigitalWife.Llm.OpenAI](src/Zhengyan.DigitalWife.Llm.OpenAI/README.md) | OpenAI-compatible LLM 客户端，支持流式对话和 function call。 |
| [Zhengyan.DigitalWife.Audio.PortAudio](src/Zhengyan.DigitalWife.Audio.PortAudio/README.md) | 跨平台录音与播放 Provider。 |
| [Zhengyan.DigitalWife.Audio.OpenAL](src/Zhengyan.DigitalWife.Audio.OpenAL/README.md) | OpenAL 音频播放 Provider。 |
| [Zhengyan.DigitalWife.Speech.SherpaOnnx](src/Zhengyan.DigitalWife.Speech.SherpaOnnx/README.md) | SherpaOnnx ASR、TTS、唤醒词实现。 |
| [Zhengyan.DigitalWife.Speech.WhisperNet](src/Zhengyan.DigitalWife.Speech.WhisperNet/README.md) | Whisper.net ASR 实现。 |

## Samples

| 示例 | 说明 |
| --- | --- |
| [Zhengyan.DigitalWife.Samples.AssistantConsole](samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md) | 控制台语音助手示例，适合先跑通 ASR、LLM、TTS。 |
| [Zhengyan.DigitalWife.Samples.RealtimeVoice](samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md) | Realtime 语音后端示例，提供 `/v1/realtime` 与 `/v1/audio/speech` 风格接口。 |
| [Zhengyan.DigitalWife.Samples.DigitalHuman](samples/Zhengyan.DigitalWife.Samples.DigitalHuman/README.md) | 数字人前端示例，负责采音、唤醒词、3D 渲染、口型和音频播放。 |
| [Zhengyan.DigitalWife.Samples.MmdQuickStart](samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/README.md) | 最小 MMD 运行示例。 |
| [Zhengyan.DigitalWife.Samples.MmdDemo](samples/Zhengyan.DigitalWife.Samples.MmdDemo/README.md) | 带 ImGui 控制面板的完整 MMD Demo。 |

## 常用命令

运行 Realtime Voice 服务：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/Zhengyan.DigitalWife.Samples.RealtimeVoice.csproj
```

运行 DigitalHuman 示例：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.DigitalHuman/Zhengyan.DigitalWife.Samples.DigitalHuman.csproj
```

运行 MMD QuickStart：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/Zhengyan.DigitalWife.Samples.MmdQuickStart.csproj
```

下载默认模型：

```powershell
./scripts/download-models.ps1
```

Linux 下：

```bash
chmod +x ./scripts/download-models.sh
./scripts/download-models.sh
```

## Linux 运行依赖

基础依赖：

```bash
sudo apt update
sudo apt install -y \
  python3 \
  libopenal1 \
  libportaudio2 \
  libasound2 \
  libsndfile1 \
  ca-certificates
```

运行 3D / GUI 入口时再安装：

```bash
sudo apt install -y \
  libgl1-mesa-dri \
  libegl1 \
  libgles2 \
  libx11-6 \
  libxi6 \
  libxcursor1 \
  libxinerama1 \
  libxrandr2 \
  libxxf86vm1 \
  libfontconfig1 \
  fonts-noto-cjk \
  mesa-utils \
  alsa-utils
```

常用排查：

```bash
dotnet --info
python3 --version
ldconfig -p | grep -Ei 'openal|portaudio|sndfile'
ldconfig -p | grep -Ei 'libGL|libEGL|libGLESv2'
fc-match "Noto Sans CJK SC"
glxinfo -B
```

## 发布和无控制台启动

开发阶段建议用 `dotnet run` 保留控制台，方便查看加载日志和脚本错误。正式发布时可使用 `dotnet publish` 发布 GamePlayer；Windows 如需双击运行不显示控制台，可把 GamePlayer 项目发布为 `WinExe` 或准备独立无控制台构建。Linux/macOS 推荐用 `.desktop`、系统启动项、后台命令或 `.app` 启动。更多说明见 [GamePlayer README](src/Zhengyan.DigitalWife.GamePlayer/README.md#隐藏控制台--无终端启动)。

## 文档

- [GameEditor README](src/Zhengyan.DigitalWife.GameEditor/README.md)
- [GamePlayer README](src/Zhengyan.DigitalWife.GamePlayer/README.md)
- [脚本 API HTML](src/Zhengyan.DigitalWife.GameEditor/script_api.html)
- [MMD Game API](docs/Zhengyan.DigitalWife.Mmd.Game.API.md)
- [渲染与资源说明](docs/Zhengyan.DigitalWife.Mmd.Game.Rendering-And-Assets.md)
- [发布与版本规范](docs/Zhengyan.DigitalWife.发布与版本规范.md)

## 技术基线

当前项目目标框架为 `net10.0`。`global.json` 以 `9.0.313` 为 SDK 基线，并通过 `rollForward=latestMajor` 允许使用 `.NET 10` SDK。