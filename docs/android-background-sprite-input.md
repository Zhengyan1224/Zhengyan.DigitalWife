# DemoGame01 左上区域镜头触摸修复

main.scene 的背景 Sprite 位于相对坐标 `(50,50)`，大小 `600×400`，DrawOrder 为 -1，TargetEntity 为空。原 GuiPointerHitTest 把所有可见 Sprite 都算作 GUI，因此在该装饰图范围内开始的触摸被 AndroidCameraTouchInput 捕获，直到松开都不会产生镜头手势。其他场景没有这张图，所以没有同样的区域问题。

SpriteLayoutResolver.IsInteractive 统一判断可见且有交互目标的 Sprite。共享 GUI 命中检测、EGL 和 Vulkan 的 Sprite 事件分发都采用这一规则。无交互目标的背景图不拦截相机；绑定目标的 Sprite 继续保留按住期间的输入捕获。

验证使用用户实际项目 `D:/Projects/CSharp/MMD/GameEditorProjects/DemoGame01` 的 main.scene，在 1280×720 与 1920×1080 下模拟四个区域的触摸开始、移动、结束，检查实际相机矩阵变化，并检查互动 Sprite 拖出边界后的捕获。既有相机射线、GUI 捕获、背景 Sprite 三项回归均通过。

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --camera-project D:/Projects/CSharp/MMD/GameEditorProjects/DemoGame01
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Pointer camera"
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android
```

修复包：`artifacts/android-player-touch-fix.apk`。代码修复应用在播放器；不需要修改或重新导出场景来移除背景图。
