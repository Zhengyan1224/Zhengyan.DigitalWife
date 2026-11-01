# Web Runtime 阶段 7：渲染功能扩展【已实现】

> 状态：**已实现**

阶段 7 为高级渲染增加了可开关的特性协议，并确保单个扩展失败不会破坏基础 PMX 场景。

已实现：

- 阴影、材质边缘、实体变换继续通过 `WebRenderFeatures` 统一开关；
- 粒子系统生成确定性的 CPU 粒子快照，并通过 WebGL2 point sprite 渲染；
- Sprite、GUI 和对话泡通过透明 2D overlay canvas 绘制；
- Skybox、Water 和 PostProcess 能力在帧状态中声明，低能力设备可以关闭；
- 高级 shader、粒子或 overlay 绘制失败时，Web runtime 保留 PMX 基础渲染和状态面板；
- overlay 使用 `pointer-events: none`，GUI 捕获时由宿主显式控制输入边界。

当前水面、天空盒和后处理先使用能力开关与安全回退路径，后续可以在同一帧协议上增加独立纹理/RenderTexture 实现，不改变场景和脚本 ABI。

验证：WebGL2 renderer、Web runtime、Web Audio、IndexedDB 模块语法检查和 smoke test 通过；Web 项目构建无错误。
