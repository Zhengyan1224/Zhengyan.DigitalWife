# MmdQuickStart：先让 MMD 角色动起来

这是学习 Mmd.Game 的入门示例。它在 C# 中直接创建场景、角色、动作、服装绑定、水面和背景音频，让你看到引擎组件如何组合。

想用可视化界面制作游戏，请用 [GameEditor](../../src/Zhengyan.DigitalWife.GameEditor/README.md)；本示例不读取 .dwgame。

## 运行

需要 .NET 10 SDK 和可用图形驱动。仓库根目录：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdQuickStart
```

无需 AI 服务或语音模型。首次构建会复制内置素材，文件较多，等待完成后再检查窗口。

## 你会看到什么

- Body 角色、服装和教室模型。
- walk / run 动作层混合。
- 相机控制、水面与场景效果。
- 背景音乐。
- 空格切换角色播放状态。

资源来自 assets/mmd/samples/GameData 和 assets/mmd/engine/Resources，构建时复制为输出目录的 GameData / Resources。缺少角色或贴图时检查整个素材目录，不能只复制 PMX。

## 如何开始改代码

阅读本目录 [Program.cs](Program.cs)，从 LoadContent 中加载模型、AddComponent 加组件、Update 中控制动作开始。换模型时替换 ContentPath 指向的实际资源，保留模型目录内的贴图。

窗口图标也来自仓库 Logo。代码示例可用不表示这些素材都有统一授权；分发时自行确认模型、动作与音乐许可。

## 常见问题

- 无法启动：确认 .NET 10 SDK 和显卡驱动。
- 没有音乐：检查系统输出和资源是否复制。
- 模型不见：查看控制台的资源加载错误。
- 想要控制面板：运行 [MmdDemo](../Zhengyan.DigitalWife.Samples.MmdDemo/README.md)。

- [项目总览](../../README.md)
- [Mmd.Game](../../src/Zhengyan.DigitalWife.Mmd.Game/README.md)
