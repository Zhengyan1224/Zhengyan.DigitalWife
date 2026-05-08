![Zhengyan.DigitalWife Logo](assets/mmd/samples/GameData/Logo/logo.png)


`Zhengyan.DigitalWife` 是一个面向跨平台数字人应用的新一代 `.NET` 引擎与示例集合，聚焦语音采集、语音识别、LLM、TTS、唤醒词、MMD/PMX/VMD 运行时和 3D Demo 的统一接入、统一配置与统一构建。它提供了一套适合 Windows、Linux 和 macOS 的目录结构、命名体系和运行入口，方便你直接搭建、扩展和替换数字人交互链路。

## 解决方案入口

- `Zhengyan.DigitalWife.sln`
- `Zhengyan.DigitalWife.slnx`

统一目标框架为 `net9.0`，`global.json` 以 `9.0.313` 为最低 SDK 基线，并允许向上使用 `.NET 10` SDK。

## 目录结构

```text
Zhengyan.DigitalWife/
├─ src/        类库源码
├─ samples/    示例与 Demo
├─ tools/      仓库维护工具
├─ scripts/    脚本
├─ models/     语音模型权重
├─ assets/     MMD 运行时资源与示例素材
├─ docs/       补充文档
└─ libs/       原生依赖辅助文件
```

## 项目导航

### 语音抽象与编排

| 项目 | 作用 | 说明 |
| --- | --- | --- |
| [Zhengyan.DigitalWife.Abstractions](src/Zhengyan.DigitalWife.Abstractions/README.md) | 公共契约层 | 音频、ASR、TTS、唤醒词、LLM 的公共接口与数据结构。 |
| [Zhengyan.DigitalWife.Assistant](src/Zhengyan.DigitalWife.Assistant/README.md) | 语音助手编排层 | 负责把音频输入、识别、分句和回复串成完整的助手流水线。 |

### Provider

| 项目 | 作用 | 说明 |
| --- | --- | --- |
| [Zhengyan.DigitalWife.Audio.PortAudio](src/Zhengyan.DigitalWife.Audio.PortAudio/README.md) | 音频 Provider | 提供跨平台录音与播放实现。 |
| [Zhengyan.DigitalWife.Llm.OpenAI](src/Zhengyan.DigitalWife.Llm.OpenAI/README.md) | LLM Provider | 提供 OpenAI 协议兼容的流式对话客户端。 |
| [Zhengyan.DigitalWife.Speech.SherpaOnnx](src/Zhengyan.DigitalWife.Speech.SherpaOnnx/README.md) | SherpaOnnx Provider | 提供识别、TTS 和唤醒词相关实现。 |
| [Zhengyan.DigitalWife.Speech.WhisperNet](src/Zhengyan.DigitalWife.Speech.WhisperNet/README.md) | Whisper.net Provider | 提供 Whisper.net 语音识别实现。 |

### MMD / 3D

| 项目 | 作用 | 说明 |
| --- | --- | --- |
| [Zhengyan.DigitalWife.Mmd](src/Zhengyan.DigitalWife.Mmd/README.md) | MMD 底层运行时 | 提供 PMX / VMD 解析、骨骼、Morph、IK、物理等基础能力。 |
| [Zhengyan.DigitalWife.Mmd.Game](src/Zhengyan.DigitalWife.Mmd.Game/README.md) | MMD 游戏层 | 基于 Silk.NET / OpenGL ES / OpenAL 的跨平台渲染与场景运行层。 |

### 示例

| 项目 | 作用 | 说明 |
| --- | --- | --- |
| [Zhengyan.DigitalWife.Samples.AssistantConsole](samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md) | 控制台语音助手示例 | 展示最基础的语音对话链路，适合先跑通 ASR、LLM、TTS。 |
| [Zhengyan.DigitalWife.Samples.DigitalHuman](samples/Zhengyan.DigitalWife.Samples.DigitalHuman/README.md) | 数字人实时对话示例 | 组合 PMX 人物、服饰绑定、唤醒词、ASR、LLM、TTS 和对话气泡，是完整的数字人入口。 |
| [Zhengyan.DigitalWife.Samples.MmdQuickStart](samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/README.md) | 最小 MMD 示例 | 只保留最小可运行路径，适合快速验证 MMD 资源和渲染链路。 |
| [Zhengyan.DigitalWife.Samples.MmdDemo](samples/Zhengyan.DigitalWife.Samples.MmdDemo/README.md) | 完整 MMD Demo | 带 ImGui 控制面板的完整示例，适合调试模型、动作和场景参数。 |

### 工具

| 项目 | 作用 | 说明 |
| --- | --- | --- |
| [Zhengyan.DigitalWife.Tools.ModelInstaller](tools/Zhengyan.DigitalWife.Tools.ModelInstaller/README.md) | 模型安装工具 | 用于下载、解包和管理仓库常用模型。 |
| [Zhengyan.DigitalWife.Tools.ModelInspector](tools/Zhengyan.DigitalWife.Tools.ModelInspector/README.md) | 模型检查工具 | 用于检查模型文件、资源目录和依赖是否完整。 |
| [Zhengyan.DigitalWife.Tools.ApiInspector](tools/Zhengyan.DigitalWife.Tools.ApiInspector/README.md) | API 检查工具 | 用于查看和验证项目内部 API 暴露情况。 |

## 资源布局

- `models/`
  语音模型权重目录，保留 `asr / wake / tts / whisper` 分类。
- `assets/mmd/engine/Resources/`
  `Zhengyan.DigitalWife.Mmd.Game` 自带资源，包括 Shader、Toon、水面、粒子、口型字典。
- `assets/mmd/samples/GameData/`
  MMD 示例工程使用的角色、场景、动作、BGM。

`Mmd` 侧运行时已经改成只按输出目录解析 `Resources/` 和 `GameData/`，不再通过查找 `*.sln` 文件名推断仓库根路径。

## 快速构建

```powershell
dotnet build Zhengyan.DigitalWife.sln
```

或：

```powershell
dotnet build Zhengyan.DigitalWife.slnx
```

## 快速开始

### 语音示例

1. 下载默认模型：

```powershell
./scripts/download-models.ps1
```

2. 复制本地配置：

```powershell
Copy-Item samples/Zhengyan.DigitalWife.Samples.AssistantConsole/appsettings.Local.example.json samples/Zhengyan.DigitalWife.Samples.AssistantConsole/appsettings.Local.json
```

3. 运行示例：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --run-once
```

### MMD QuickStart

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/Zhengyan.DigitalWife.Samples.MmdQuickStart.csproj
```

### MMD Demo

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdDemo/Zhengyan.DigitalWife.Samples.MmdDemo.csproj
```

## 补充文档

- [MMD Game API 详细说明](docs/Zhengyan.DigitalWife.Mmd.Game.API.md)
- [QuickStart 说明](docs/Zhengyan.DigitalWife.Samples.MmdQuickStart.md)
- [资源与渲染说明](docs/Zhengyan.DigitalWife.Mmd.Game.Rendering-And-Assets.md)
- [发布与版本规范](docs/Zhengyan.DigitalWife.发布与版本规范.md)

## 当前验证状态

已验证：

- `dotnet build Zhengyan.DigitalWife.slnx -v minimal`
  可通过。

## 说明

- 原 `Zhengyan.MmdViewer` 不再纳入新仓库结构。
- 示例项目和 Provider 项目都已经切换到 `Zhengyan.DigitalWife.*` 命名空间前缀。
- `AssistantConsole` 同时兼容新的 `DIGITALWIFE_` 与旧的 `SPEECHBRIDGE_` 环境变量前缀。


