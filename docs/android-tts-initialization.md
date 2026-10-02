# Android TTS 初始化与 DemoGame01 跳舞按钮

DemoGame01 的 `scripts/Body.csx` 先调用 `Entity.Speak("准备播放舞蹈", onCompleted)`，
在语音播放完成回调中才执行 `Audio.Play("Lamb")` 和 `Entity.ApplyMotion(...)`。
因此 TTS 初始化失败会导致该按钮无法进入音乐/动作播放阶段。

原播放器存在两处问题：

- Manifest 没有声明 `android.intent.action.TTS_SERVICE` 的 `queries`，会影响 Android 11+
  上的 TTS 引擎发现。[Android 官方要求](https://developer.android.com/reference/android/speech/tts/TextToSpeech)
- 初始化结果保存在一次性的 `TaskCompletionSource<bool>` 中；第一次失败后，后续请求始终读取该失败结果。

现在 Manifest 包含 TTS 服务查询声明。初始化在主线程进行，并发请求共享同一次初始化；
失败或 10 秒超时后，下一次语音请求可以重建引擎。取消一个调用者不会取消其他调用者等待的初始化。
每次引擎初始化使用独立监听器和完成对象，丢弃已被替换的引擎回调。
初始化完成通知始终排队到主线程，避免构造函数同步回调时引擎对象尚未赋值。

播放完成回调仍只在 `UtteranceProgressListener.OnDone` 后触发。
失败和取消不会假装播放成功；停止或切换场景后，之前等待初始化的语音不会突然开始播放。
这些修改只需更新 Android Player，现有 dwgame 包不必重新导出。

播放器日志会显示 `TTS ready: default=...; engines=...`，或具体初始化失败原因。
若日志显示没有可见引擎，需要在手机的文字转语音设置中启用可用引擎；
修复服务可见性并不等于给手机安装语音引擎或语言数据。

验证命令：

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "TTS "
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --compile-project D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame01
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk
adb logcat -v threadtime ZhengyanGamePlayer:I TextToSpeech:V '*:S'
```

回归覆盖初始化失败后重试、并发共享、独立取消、初始化超时以及过期回调。
系统 TTS 服务发现、中文语音播放和实际舞蹈回调仍需 Android 真机验证。

本次验证：运行时回归 16/16 通过，DemoGame01 的 11 个脚本全部编译通过。
ARM64 Release APK 已成功生成，APK v2/v3 签名校验通过，
使用 `aapt2 dump xmltree` 确认最终安装包中包含 TTS 服务查询声明。
构建仍有原有 SDL 重复库及 16 KB 页对齐警告；当前没有连接 Android 设备。
