# Android 输入框与脚本时间 API 修复（2026-10-16）

加密包密码框继续使用原生密码编辑和掩码。部分 IME 对字母使用 `CommitText`、对数字使用 `SendKeyEvent`；新增 `AndroidTextInputConnection` 将数字按下直接提交到当前编辑连接，消耗配对的抬起事件。其它按键和输入连接操作仍由原生连接处理，Shift 符号和 Ctrl/Alt/Meta 组合键不会被转换成数字。密码框与场景文本编辑框都使用该连接。

场景输入框以前覆盖一个写死背景和字体大小的 `EditText`，同时 Canvas 仍绘制原文字，导致字体变大、背景变化和文字重叠。编辑时现在保留 Canvas 的背景、圆角和边框，并暂停该输入框的 Canvas 文字绘制；透明原生编辑器使用相同的像素字号、颜色和对齐方式，支持尺寸与脚本内容变化。点击外部、完成输入、返回、暂停应用、控件隐藏或移除、场景或项目变化时清理编辑器和输入法组合状态。

`Body1.csx` 的 `DateTimeOffset.AddSeconds(double)` 在上一版 Release 的 `System.Private.CoreLib.dll` 中确实不存在。为动态加载的用户脚本保留完整 `DateTimeOffset`、`DateTime` 和 `TimeSpan`，新增对实际 Android 裁剪输出的检查，覆盖时间运算、`MinValue` 及 `ScriptBinding.Enabled` 的读写成员。

DemoGame01 的第三个场景确实配置了 `Body_2.csx` 的 `enabled: false`。按钮未指定目标实体，因此点击事件会广播；启用的 `foreignhouse.csx` 与 `MaidOutfit.csx` 都在点击时调用 `Entity.Speak("按钮被点击了", ...)`。听到这句话不能判断禁用脚本被执行。若不需要这两处示例语音，应移除这两个启用脚本中的对应调用，或将按钮目标设为实际处理事件的角色。

验证结果：运行时回归 30/30 通过；DemoGame01 的 11 个启用脚本原样编译通过；加密包测试覆盖脚本先预编译后禁用、旧 DLL 仍在包中时，加载脚本和对象脚本的禁用状态仍被保留；Release ARM64 构建 0 错误，保留已有 SDL 打包和页面大小警告。

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -c Release
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -c Release --no-build -- --verify-linked-script-apis src/Zhengyan.DigitalWife.GamePlayer.Android/obj/Release/net10.0-android/android-arm64/linked
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -c Release --no-build -- --compile-project ../GameEditorProjects/DemoGame01
```

安装包：`artifacts/android/GamePlayer-arm64-20261016-input-script-fixes-Signed.apk`。更换播放器即可复测现有包中的输入框和时间 API，无须为这两类修复重新导出。

当前没有连接 adb 设备，手机端仍需复测：纯数字及数字/字母/符号混合密码、删除与选择替换；横屏输入框字号/背景与中文组合输入；隐藏键盘及点击外部后显示；场景切换后的残留；`Body1.csx` 的时间运算。
