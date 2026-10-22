# GameEditor：可视化游戏编辑器

GameEditor 是本项目的内容制作入口。它把模型、动作、场景、界面、脚本和 AI 配置保存成游戏工程，再交给 PC / Android GamePlayer 运行。

适合想制作 MMD 互动场景、角色对话应用或轻量游戏的人。你可以先通过界面搭建场景，游戏规则和对话流程再用 C# 脚本补充。

## 启动前准备

需要 .NET 10 SDK 和可用的桌面图形驱动。第一次构建会下载依赖。只搭建场景无需 AI 服务和语音权重；Python 只在使用 Python 脚本时需要。

下面的命令在[仓库根目录](../../README.md)执行：

```text
dotnet run --project src/Zhengyan.DigitalWife.GameEditor
```

直接打开自己的工程目录：

```text
dotnet run --project src/Zhengyan.DigitalWife.GameEditor -- --project "D:/Games/MyFirstGame"
```

选择渲染后端：

```text
dotnet run --project src/Zhengyan.DigitalWife.GameEditor -- --graphics-backend OpenGL
dotnet run --project src/Zhengyan.DigitalWife.GameEditor -- --graphics-backend Vulkan
```

不传目录时默认使用程序输出目录下的 `GameEditorProjects/DemoGame`。正式作品建议另选文件夹，避免构建清理时丢失工程。

## 第一次制作场景

1. 在 `Project` 面板输入工程目录，点击 `Use Directory`，再 `New Project` 或 `Load`。
2. 在 `Assets` 面板添加 PMX 模型。PMX 是角色或场景模型，贴图应与模型一起导入。
3. 导入 VMD 动作，给角色选择动作和循环播放。
4. 在 `Hierarchy` 选择实体，在 `Inspector` 调整位置、旋转、大小。
5. 调整相机与灯光，让角色进入预览画面。
6. 保存，使用 GamePlayer 运行工程目录，检查实际脚本与交互。

```text
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer -- "D:/Games/MyFirstGame"
```

编辑器预览方便布置场景；GamePlayer 是游戏脚本与交互行为的运行验证入口。保存后再测试，避免播放器读到旧配置。

## 面板怎么用

| 面板 | 用途 |
| --- | --- |
| Project | 工程目录、加载/保存、窗口设置、语音和 AI、打包发布 |
| Scenes | 新建/切换场景，设置 GamePlayer 启动场景 |
| Assets | 导入模型、动作、音频、图片，添加水面、粒子、灯光等 |
| Hierarchy | 找到并选择场景实体 |
| Inspector | 编辑所选实体的变换、资源、脚本、Collider 和专属属性 |
| Viewport | 查看场景、调整相机和检查布局 |

`Editor scene` 与 `GamePlayer startup scene` 是两个设置：前者是你正在编辑的场景，后者是玩家启动时进入的场景。场景切换前编辑器会保存当前场景。

## 可以编辑的内容

- PMX 角色、VMD、多动作层、口型、服装绑定和 PMX 物理。
- 空对象、多个 Collider、Mesh Collider、地面采样与 NavMesh。
- 天空盒、环境光、平行光、点光和射灯、阴影。
- 粒子、水面波纹、平面镜、3D 图片平面。
- 多相机、分屏、离屏纹理 Render Texture。
- 按钮、文本框、标签、进度条、对话气泡、2D Sprite 和布局。
- 场景加载画面、实体脚本和加载脚本。
- ASR、TTS、LLM、RealtimeVoice、Skills 与记忆。
- PC 桌面精灵与托盘设置。

场景碰撞查询、PMX 刚体物理和视觉水面效果用途不同。属性详解请用[脚本 API 检索](script_api.html)，不要只根据效果名称推断物理行为。

## 工程保存在什么地方

```text
MyFirstGame/
  game.project.json       工程设置与场景入口
  scenes/                 各场景的 JSON
  assets/                 模型、贴图、动作、音频、shader、语音模型
  scripts/                C# .csx / PC Python .py
  skills/                 可选 AI 工具说明与资源
  saves/                  工程目录运行时的存档
  compiled/               导出时生成的脚本程序集
```

导入时勾选 `Copy imported files into project` 可立即复制资源。保存时也会归档已引用的外部资源并改成工程相对路径；PMX 复制整个模型目录，减少贴图遗漏。

脚本中动态拼接的任意文件路径不能依靠编辑器自动发现。自己把需要的文件放入工程目录。推荐 `assets/...` 或 `project:assets/...`；`app:` 指播放器内置资源，`rt:` 指运行时纹理。

## 写第一个脚本

在实体 Inspector 添加 C# 脚本，使用生成的模板。下面示例每帧缓慢转动角色：

```csharp
if (IsUpdate)
{
    Entity.RotateY(20.0f * (float)DeltaSeconds);
}
```

脚本每次事件都会执行，通过 `IsStart`、`IsUpdate`、`IsGuiEvent` 等区分。跨帧状态可按[生命周期文档](api_docs/04-lifecycle.md)保存。

PC 支持 C# 和本机 Python；Android 只支持 C#。添加或保存时的轻量语法检查不能替代导出编译和播放器运行测试。

[Touch 示例](api_docs/09-input.md)涵盖点击、滑动、长按和双指手势；[平台标识](api_docs/03-system-api.md)提供 `PlatformName / IsAndroid / IsWindows / IsLinux / IsMacOS`。

## 给角色增加对话

先确认普通场景正常，再逐项开启：

1. Voice / TTS：配置人物说话，脚本调用 `Entity.Speak`。
2. LLM / OpenAI-compatible：填写服务地址、模型名和密钥，再用脚本显示回复。
3. ASR：接入语音输入；PC 本地识别需要对应模型。
4. 或直接配置 RealtimeVoice 服务，由服务处理 ASR、LLM 和 TTS。

Skills 与 memory 有独立开关。Skills 放在游戏工程的 `skills/<名称>/SKILL.md`，长期记忆写入玩家存档目录下的 `memory/`。命令工具按播放器权限执行，适合可信工程。

详细流程见 [ASR](api_docs/24-asr.md)、[LLM](api_docs/25-llm.md)、[RealtimeVoice](api_docs/26-realtime-voice.md)。唤醒后等待多久、是否可以打断，是脚本控制的交互逻辑，不是所有游戏的固定行为。

## 导出游戏包

在 `Project -> Package / Publish` 配置并导出：

| 选项 | 用途 |
| --- | --- |
| Output package | 输出 `.dwgame` 文件 |
| Encrypt package | 加密；运行时需要密码 |
| Split package | 输出 `.dwgame.001`、`.002` 等分片 |
| Include saves | 把开发存档一起打包；一般发布时关闭 |
| Precompile desktop C# | 提前编译 PC 脚本，减少首次加载耗时 |
| Precompile Android C# | 提前检查/编译 Android 脚本 |
| Check Android Compatibility | 检查 Python、桌面窗口等 Android 差异 |

`.dwgame` 是内容包，不是独立的可执行文件。发布时一起提供合适的播放器。不要把服务密钥、个人记忆或测试存档不加检查地发布出去。

PC 与 Android 使用独立脚本程序集，源码作为兼容回退保留在包中。更新脚本 API 后应更新编辑器与播放器，重新导出用到新 API 的包。

## 常见问题

| 现象 | 建议 |
| --- | --- |
| 启动后看不到角色 | 检查模型加载日志、相机位置、缩放和 Visible |
| 模型贴图丢失 | 保留整个模型目录，重新导入并保存 |
| 编辑场景正确，运行却是另一个场景 | 检查 GamePlayer startup scene |
| 脚本没有执行 | 检查绑定 Enabled、保存路径和 GamePlayer 日志 |
| 镜面或水面导致卡顿 | 降低反射纹理尺寸，减少反射面、阴影和额外相机 |
| Android 导出报错 | 查看兼容性结果；Python 脚本需改为 C# |
| Vulkan 无法启动 | 使用 OpenGL 参数启动，再检查驱动 |

Windows EXE 图标由工程文件中的 `ApplicationIcon` 嵌入；游戏窗口图标可在工程中配置。Linux / macOS 分发方式见[PC 播放器发布说明](../Zhengyan.DigitalWife.GamePlayer/README.md#发布桌面播放器)。

## 开发与文档

```text
dotnet build src/Zhengyan.DigitalWife.GameEditor
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests
```

- [项目总览](../../README.md)
- [PC 播放器](../Zhengyan.DigitalWife.GamePlayer/README.md)
- [Android 播放器](../Zhengyan.DigitalWife.GamePlayer.Android/README.md)
- [脚本 API HTML](script_api.html) / [模块索引](script_api.md)
- [资源来源](../../assets/mmd/engine/Resources/README.md)
