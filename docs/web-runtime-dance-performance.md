# Web 跳舞启动和播放性能

跳舞按钮触发音频播放和动作切换。此前音频每次 `play` 都重设 `src`，并让帧更新等待媒体缓冲完成；人物还在主线程计算约 4.1 万顶点的蒙皮并上传完整网格。

本次修改：

- 加载阶段准备音频元素和 AudioContext，复用相同 URI；重复播放通过回到第 0 秒重新开始，不重载音源。重叠播放请求复用待完成的 Promise。
- C# 发出异步播放命令后继续帧更新。真实播放错误通过浏览器回调报告；由停止、暂停或音源切换引起的旧请求取消不会误报。
- 加载阶段准备注册动作的轨道，点击时切换并重置缓存中的姿势求解器，不在事件回调中重新求解和上传整个人物。
- BDEF1/2/4 使用 GPU 蒙皮。骨骼矩阵通过 RGBA32F 纹理传递，支持这个人物的 237 根骨骼，不受传统 uniform 数组大小限制。每帧骨骼数据为 15,168 字节，替代 1,309,664 字节的完整顶点更新。
- 位置变形只提交变化顶点，更新受影响的纹理行。描边和阴影使用同一个蒙皮函数。SDEF/QDEF、UV 变形和超出纹理容量的模型保留 CPU 路径。
- 重用骨骼求解缓冲区。拓扑依赖满足条件时，Web 的 IK 调整只重建受影响的分支；不满足条件时保留完整求解。IK 迭代次数不变，其他平台默认仍使用完整求解。
- 保留动态脚本依赖的 `System.Console`。发布裁剪原先删除 `WriteLine(string)`，让 GUI 脚本在执行音频和动作命令之前失败。脚本诊断现在会进入日志和页面错误信息。

验证命令：

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
node --test tests/web-audio.test.mjs tests/web-renderer.test.mjs tests/web-runtime-input.test.mjs
dotnet publish src/Zhengyan.DigitalWife.GamePlayer.Web -c Release -o artifacts/web-player
```

C# 对照真实人物连续 46 帧的完整 IK 与分支 IK，并确认动画更新只提交骨骼和稀疏变形数据。真实 WebGL2 transform feedback 对照 BDEF1/2/4 顶点、237 根骨骼及三个动作帧，最大位置/法线误差约 0.0000022。JavaScript 回归覆盖音频重叠请求、缓冲、取消、失败、重新播放、骨骼纹理与变形纹理更新。

正式性能测试使用 Release AOT 包；Debug 的解释执行不能代表发布版动画性能。本次仍使用现有的 Web 轻量级物理求解器。

最终复测：Edge / Intel HD Graphics 520 / ANGLE D3D11，874 × 612 Canvas，实际点击跳舞并确认音乐时间前进及骨骼纹理持续更新。首次姿势提交约 214.5 ms；连续播放四次采样的帧间隔中位数为 18.4–19.1 ms（约 52–54 FPS）；播放中拖拽为 19.9 ms，P95 为 28.8 ms。音频、脚本和 WebGL 均无错误。GPU 输出与 CPU 结果最大误差为 0.0000022。结果包含浏览器分析器的采样开销，实际性能随设备与分辨率变化。
