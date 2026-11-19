# PC、Android、Web 相机输入

三端使用 `GameProjects/CameraControlPolicy.cs` 执行相机控制，鼠标由 `CameraInputPolicy.Mouse` 解释，触屏由 `CameraInputPolicy.Touch` 解释。输入按设备事件处理，不按操作系统限制：Android 可接鼠标键盘，PC 可接触屏，Web 可同时接收鼠标和触屏。

| 模式 | 鼠标与键盘 | 触屏 |
| --- | --- | --- |
| editor / orbit / max | 右键旋转，中键平移，Alt＋左/中键旋转，Alt＋右键缩放；滚轮缩放；W/S 拉近拉远，A/D 左右平移，Q/E 上下平移 | 单指旋转，双指平移与捏合缩放，方向与原 Android 规则一致 |
| free_fly | 右键观察；WASD 前后左右，Q/E 升降；关闭 RequireRightMouseForMouseLook 后可直接观察 | 单指观察，双指在相机平面移动，捏合沿观察方向移动 |
| fps / first_person | 右键观察，位置由目标实体决定 | 单指观察 |
| fps_control / locked_fps | 锁定指针后直接观察；实体移动由游戏脚本控制 | 单指观察；触屏不需要指针锁定 |
| tps / third_person / shoulder | 右键观察，滚轮调节跟随距离 | 单指观察，捏合调节跟随距离 |
| rts | 右键旋转，中键平移；WASD 在地面移动，滚轮调节距离 | 单指观察，双指平移，捏合调节距离 |
| top_down / isometric / side_scroller | 保持模式的观察方向，滚轮调节距离 | 保持观察方向，捏合调节距离 |
| orbital_follow / auto_orbit | 自动环绕，滚轮调节距离 | 自动环绕，捏合调节距离 |
| lock_on / cinematic_follow | 由目标和模式配置控制 | 由目标和模式配置控制 |
| fixed / custom / vmd | 固定相机、脚本或动画接管 | 固定相机、脚本或动画接管 |

单指和双指之间切换时重新建立手势起点。GUI 和交互 Sprite 从按下起捕获对应指针，移出边界仍保持捕获；装饰 Sprite 不阻挡相机。Web 保留整帧的鼠标动作和修饰键，即使按下、拖动、抬起都发生在同一帧也不会丢失动作。触屏和鼠标原始状态分开传递，避免触屏又被算作左键拖动。

桌面精灵的 `window.desktopSpriteDragButton` 优先于相机鼠标拖动。PC 移动系统窗口，Web 的 `desktop-sprite.js` 移动宿主网页内的 iframe；直接打开播放器时移动画布容器。`none` 禁用，支持与 PC 相同的 left/right/middle 别名。GUI 和菜单优先处理点击。触屏用于相机手势。Web 拖动保持指针捕获，结束后恢复透明区域点击穿透，位置限制在页面可见范围内。

## 验证

- `dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --test "Camera input"`：16 种模式的鼠标组合、修饰键、键盘和触屏，以及与原生 OrbitCamera 的数学结果比较。
- `dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --test "Menu exit"`：实际 ImGui 弹窗中的退出请求在完整绘制后才关闭窗口。
- `node --test tests/web-runtime-input.test.mjs tests/web-asr.test.mjs tests/web-offline.test.mjs`：输入采样、捕获、手势、语音生命周期。
- `node tests/web-desktop-sprite-browser.mjs`：浏览器 DPR 1/2 下的页面点击穿透、GUI、菜单、精灵移动和麦克风重试。
- `node tests/web-desktop-sprite-browser.mjs --aot-smoke`：最终 AOT 发布文件中的真实 JS/.NET 鼠标动作记录、键盘和触屏数据改变相机。仅通过测试服务器替换为最小场景，不改写发布目录的游戏数据。
- `node tests/web-desktop-sprite-browser.mjs --probe-microphone`：在独立浏览器配置中检查真实设备，不录制或保存音频。
