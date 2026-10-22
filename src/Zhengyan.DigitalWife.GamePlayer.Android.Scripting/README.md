# Android.Scripting：Android C# 脚本接口与编译器

这个库让 PC 编辑器提前编译 Android 游戏脚本，也让 Android 播放器使用同一套强类型接口运行它们。它本身不是 APK。

想写游戏脚本，直接看[脚本 API](../Zhengyan.DigitalWife.GameEditor/script_api.html)。想扩展 Android 脚本接口，再阅读本目录源码。

## 主要职责

- `AndroidScriptGlobals`：Entity、Scene、Input、Audio 和各类事件字段。
- `AndroidScriptEntity / Scene`：映射到共享实体和场景对象。
- `AndroidScriptInputApi`：键鼠、手柄、Touch 与剪贴板平台契约。
- `AndroidScriptServices`：语音、LLM、存档、网络、Render Texture 等服务入口。
- `AndroidScriptCompiler`：生成导出包里的 C# 脚本程序集。
- `AndroidScriptMetadata`：为运行时编译提供程序集元数据。

编译器也会处理桌面 .NET 与 Android 运行时之间的部分语言兼容差异。源码仍保留在包中，旧 DLL 不兼容时可回退编译；重新导出能提前发现问题并减少首次启动耗时。

## 开发使用

本库目标是 `net10.0`，编译它不需要 Android SDK。本地 `AndroidScriptAsr / AndroidScriptTts` 使用工程内 Sherpa 模型；原生 AudioRecord / AudioTrack 与触摸采集适配位于 [Android 播放器](../Zhengyan.DigitalWife.GamePlayer.Android/README.md)。它们不调用系统识别或网络 TTS，LLM 是独立的远端接口。

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android.Scripting
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Script"
```

## 新增 API 时的检查

先修改契约与宿主实现，再更新脚本文档和导出编译验证。动态加载的脚本成员无法全部从静态调用图推断，Release 裁剪也需要保留新成员并验证。

`PlatformName / IsAndroid` 识别的是 Android 运行时契约，即使在 Windows 编辑器中编译也不会返回 Windows。旧包使用的 `AndroidScriptGlobalsContract` 仍保留兼容填充。

PC 与 Android 的程序集类型不是任意可互换的。编辑器同时引用桌面与 Core 类型时使用项目别名，避免同名类型混用。

- [项目总览](../../README.md)
- [Touch 与手势](../Zhengyan.DigitalWife.GameEditor/api_docs/09-input.md)
- [运行平台标识](../Zhengyan.DigitalWife.GameEditor/api_docs/03-system-api.md)
- [Android 回归与裁剪验证](../../tests/Zhengyan.DigitalWife.AndroidRuntime.Tests/README.md)
