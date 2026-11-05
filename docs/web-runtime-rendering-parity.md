# Web 静止人物、光照和镜头拖拽修复

## PC 行为依据

`PmxModelComponent.UpdateAnimationStep` 只在 `IsPlaying && EnablePhysical && !_skipPhysicsOnNextPlayFrame && _motionLayers.Count != 0` 时推进物理。静止和暂停的姿势不需要反复求解或上传；停止后重新计算第 0 帧。

Web 现在遵循这些启用条件。首次加载和播放的第一帧不积分重力，恢复播放时同步时间，避免将暂停期间的时间推进到动作中。暂停保留最后的网格，停止和物理重置只提交一次新姿势。实体播放速度只应用一次。

## 光照和阴影

- PMX 的漫反射、环境光、描边和高光颜色以显式数组跨越 JS interop，避免默认 JSON 序列化忽略 `System.Numerics` 向量字段。
- 环境光使用 PC 的 `albedo * materialAmbient * ambientColor * ambientStrength` 公式。阴影影响直射光，不压暗环境光。高光和局部灯光衰减也使用对应的 PC 公式。
- 方向光阴影覆盖经过实体变换的模型包围盒，使用 WebGL 深度范围、场景阴影透明度、法线偏移、斜率偏差和 3×3 PCF。
- 阴影深度通道遵循材质投影开关、双面设置和纹理透明度。主通道和阴影通道使用相同的顶点属性位置，避免共享 VAO 的法线槽被 UV 覆盖。粒子使用独立的 VAO。
- 与 PC 一样，实体的接收阴影开关作用于整个 PMX 模型，教室地板不再因 PMX 文件的材质自阴影标志而漏掉人物投影。
- 缓存静态投影深度。相机移动继续绘制主画面，模型变形、变换和光源投影变化会重新生成阴影。

## 验证

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
node --test tests/web-renderer.test.mjs tests/web-runtime-input.test.mjs
dotnet publish src/Zhengyan.DigitalWife.GamePlayer.Web -c Release -o artifacts/web-player
```

C# 回归覆盖真实 Nene 模型的无动作、首帧、暂停、恢复和停止，以及材质 JSON 和经过缩放、平移的阴影覆盖范围。JavaScript 回归覆盖输入合并、纹理、材质状态和阴影缓存。`tests/helpers/web-renderer-gpu-fixture.mjs` 在真实浏览器中验证环境光、直射光、阴影缓存和透明投影的像素结果。

本机最终 Release AOT 复测使用 Edge、Intel HD Graphics 520 / ANGLE D3D11、874 × 612 Canvas 和同一教室/人物场景：

| 状态 | 帧间隔中位数 | 帧间隔 P95 |
| --- | --- | --- |
| 拖拽前 | 17.1 ms | 24.1 ms |
| 拖拽中 | 16.4 ms | 27.9 ms |
| 拖拽后 | 17.0 ms | 28.4 ms |

GPU 像素测试得到阴影区 RGB 64、受光区 RGB 191、缓存阴影 RGB 64、透明投影孔 RGB 191，符合设定公式。浏览器异常、WebGL 错误和无效顶点均为 0。实际帧率会随设备、Canvas 分辨率和场景变化。

## 当前范围

Web 播放中的物理仍使用现有轻量级回退求解器；本次对齐物理启用和暂停规则，没有将 PC 的 Bullet 刚体与关节求解器移植到 Web。Toon/Sphere 贴图和局部灯光投影也需要后续独立对齐。
