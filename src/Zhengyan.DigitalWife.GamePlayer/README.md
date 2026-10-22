# GamePlayer：PC 游戏播放器

GamePlayer 读取 GameEditor 制作的工程目录或 `.dwgame`，运行场景、角色动作、游戏脚本、界面、音频和 AI 交互。它面向 Windows、Linux 和 macOS。

想制作游戏请先看 [GameEditor](../Zhengyan.DigitalWife.GameEditor/README.md)；手机运行请看 [Android 播放器](../Zhengyan.DigitalWife.GamePlayer.Android/README.md)。

## 运行一个游戏

### 从源码运行

安装 .NET 10 SDK，在仓库根目录执行。用自己的目录替换示例路径：

```text
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer -- "D:/Games/MyFirstGame"
```

这个目录必须包含 `game.project.json`。也可以直接传游戏包：

```text
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer -- "D:/Games/MyFirstGame.dwgame"
```

不传参数时读取播放器输出目录下的 `GameEditorProjects/DemoGame`。它不是编辑器输出目录中的同名文件夹；新手最好显式指定工程路径。

### 使用已发布的播放器

保留发布目录中的 EXE / DLL、原生库和 `Resources/`，不要只复制一个 EXE。需要 .NET Runtime 与否取决于提供者是否做了 self-contained 发布。

```powershell
./Zhengyan.DigitalWife.GamePlayer.exe "D:/Games/MyFirstGame.dwgame"
```

Linux / macOS 使用对应平台的发布产物：

```bash
./Zhengyan.DigitalWife.GamePlayer "/path/to/MyFirstGame.dwgame"
```

`D:/Games/...` 与 `/path/to/...` 都是示例路径，不是仓库自带工程。

## 启动参数

| 参数 | 作用 |
| --- | --- |
| 第一个路径参数 | 工程目录、`.dwgame` 或第一个 `.dwgame.001` 分片 |
| `--package-password "密码"` | 解密游戏包 |
| `--graphics-backend OpenGL` | 指定 OpenGL |
| `--graphics-backend Vulkan` | 指定 Vulkan，需驱动支持 |

```text
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer -- "D:/Games/MyFirstGame.dwgame" --package-password "your-password"
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer -- "D:/Games/MyFirstGame" --graphics-backend OpenGL
```

使用分包时把所有分片放在同一目录，再传基础包路径或第一片。缺片、错误密码或文件损坏都会导致加载失败。

## 播放器运行哪些能力

- PMX 模型、VMD、动作层、表情、骨骼绑定、PMX 物理和口型。
- 多场景、加载画面、灯光、阴影、天空盒、水面、粒子、镜面和 Render Texture。
- C# / Python 游戏脚本、GUI、气泡、2D Sprite、存档和网络。
- Collider、射线拾取、地面采样和 NavMesh。
- ASR、TTS、OpenAI-compatible LLM、工具调用、Skills、记忆和 RealtimeVoice。
- 普通游戏窗口和桌面精灵窗口。

能力是否启用由游戏工程与脚本决定。仅配置服务地址不会自动生成完整对话流程。

## 脚本与输入

C# `.csx` 在进程内运行，Python `.py` 使用本机 `python` 或 `python3` 进程。Python 未安装只影响使用该语言的脚本。

导出时 PC C# 编译到 `compiled/desktop/`。播放器优先使用有效程序集；缺失或源码已更新时回退到源码编译。Android 程序集位于独立的 `compiled/android/`，两者不混用。

Touch 接入 Windows 原生指针、Linux/X11 和 macOS Cocoa。macOS 主要是触控板，Wayland Touch 暂不支持。常见用法：

- `Input.Touches`：当前帧最终触点和像素位移。
- `Input.TouchTransitions`：有序按下/释放/取消，避免漏掉快速点击。
- `Input.TryGetTouch(id, out touch)`：稳定 ID 跟踪。
- `PlatformName`、`IsWindows / IsLinux / IsMacOS / IsAndroid`：只读运行平台标识。

详细脚本与 GUI 示例见[Input](../Zhengyan.DigitalWife.GameEditor/api_docs/09-input.md)、[生命周期](../Zhengyan.DigitalWife.GameEditor/api_docs/04-lifecycle.md)和[检索页面](../Zhengyan.DigitalWife.GameEditor/script_api.html)。

## 语音与 AI

PC ASR 使用工程配置的 Whisper / Sherpa 模型与本地麦克风。人物说话由 TTS 合成，播放后驱动 PMX 口型；模型需要对应的嘴部 Morph。

模型路径推荐显式使用 `project:assets/tts/...` 或 `app:...`。普通 TTS 相对路径会先按程序目录解析，再尝试工程目录，不要依赖不同电脑上恰好相同的外部路径。

LLM 服务地址、模型名和密钥来自工程配置。RealtimeVoice 将语音处理交给远端服务，PC 仍负责本地录音与播放。查看[服务示例](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)了解部署方式。

开发阶段先分别验证录音、识别、文本回复与说话，再串联完整流程。每轮等待时间、唤醒词和打断行为由游戏脚本决定。

## 存档、记忆与游戏包缓存

- 工程目录运行：默认存档写入工程 `saves/`。
- 包运行：存档写入用户数据目录，与临时解包目录分开。
- 长期记忆：启用后写入存档目录的 `memory/`。
- 未加密包：使用本地缓存，后续按包指纹复用。
- 加密包：使用运行时临时解包目录，退出时尝试清理。

开发存档不是游戏资源，正式导出一般不勾选 Include saves。加密降低直接修改包内容的便利程度，不能承诺运行环境中的内容绝对不可提取。

## 桌面精灵模式

工程可配置透明、无边框、置顶、点击穿透、鼠标拖动和托盘菜单。正常游戏不需要开启。

Windows、macOS 和 Linux 使用各自窗口实现。Linux 点击穿透目前依赖 X11；Wayland 原生输入区域没有接入。图形后端和窗口管理器也会影响透明效果。

## 发布桌面播放器

在仓库根目录执行，以 Windows x64 为例：

```text
dotnet publish src/Zhengyan.DigitalWife.GamePlayer -c Release -r win-x64 --self-contained true -o artifacts/player-win-x64
```

Linux x64 改为 `linux-x64`；macOS 根据机器选择 `osx-x64` 或 `osx-arm64`。self-contained 表示包含 .NET 运行时，不代表包含显卡驱动、系统音频库或游戏资源。

保留整个发布目录，游戏内容另外提供。新 C# API 需要同步更新编辑器、播放器与导出包。

### 隐藏控制台 / 无终端启动

- Windows 可发布为 GUI 程序：在上述 publish 命令加 `-p:OutputType=WinExe`，开发时保留普通 Exe 方便看日志。
- Linux 可创建 `.desktop` 启动器，填写实际播放器路径和游戏路径，设置 `Terminal=false`。
- macOS 可将发布产物包装为 `.app` 并配置图标；当前 `dotnet publish` 本身不自动提供完整 Finder 应用包装。

EXE 图标与工程内的游戏窗口图标是两个设置。Windows EXE 使用仓库 `favicon.ico`；Linux 启动器和 macOS bundle 需各自配置。

## 常见问题与验证

| 现象 | 先检查 |
| --- | --- |
| 找不到工程 | 路径是否含 `game.project.json`，是否误用编辑器/播放器的默认输出目录 |
| 黑屏或 Vulkan 初始化失败 | 控制台日志，尝试 OpenGL，更新图形驱动 |
| 模型贴图丢失 | 是否保留 PMX 所在目录及其贴图 |
| 麦克风不可用 | 系统权限、设备选择、支持的采样率；先做系统录音测试 |
| Python 脚本失败 | `python --version` 或 `python3 --version`，以及脚本错误日志 |
| 语音服务连不上 | 地址、端口、模型名、鉴权和网络；本地识别模型也应存在 |
| 加密包加载失败 | 密码、分片完整性、包是否损坏 |
| 场景与 PC/Android 画面不同 | shader 后端、纹理、反射/阴影质量及设备 GPU |

Linux 依赖见[主 README](../../README.md#linux-运行依赖)。记录问题时保留完整控制台日志；发布为无控制台程序前先用普通开发入口复现。

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

回归程序包含部分共享脚本和图形测试，不能替代对自己的工程进行运行测试。

- [项目总览](../../README.md)
- [编辑器](../Zhengyan.DigitalWife.GameEditor/README.md)
- [渲染后端](../../docs/rendering-backends.md)
