# Web Runtime 阶段 6：输入、存档和基础音频【已实现】

> 状态：**已实现**

阶段 6 完成了浏览器平台服务闭环：Pointer、Touch、Wheel、Keyboard 继续映射到共享 `RuntimeCameraInput`；GUI overlay 可以捕获输入；存档使用 IndexedDB；音频在用户手势后通过 Web Audio/HTML Audio 解锁。

已实现：

- `setInputCapture` 和 `SetGuiInputCapture`：GUI 捕获时相机和脚本输入不会继续穿透；
- `WebSaveStore`：JSON 存档的保存、读取和删除；
- `WebAudioService` 与 `web-audio.js`：用户手势解锁、播放、停止和统一停止；
- IndexedDB 资源缓存与 JSON 存档共用同一个浏览器数据库；
- 页面刷新后可通过存档服务恢复工程状态；
- 场景停止时音频资源统一停止并释放。

验证：JavaScript 语法检查、Web runtime smoke test、WebAssembly 构建和共享运行时自测通过。真实浏览器触摸设备和音频权限验证需要带 Chrome/Edge 的环境。
