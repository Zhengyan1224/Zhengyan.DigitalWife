# Web 静止人物、光照和镜头拖拽修复

2026-11-11：DemoGame01 的背景 Sprite、服装骨骼、天空、水面/水下、粒子、多相机与 RenderTexture 续作见 [当前能力审计](web-runtime-capability-gap.md)。以下保留历次验证记录，不代表本次全部功能或性能结论。

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

以上记录描述当时的修复范围。局部灯光投影已在下述修复中接入；Toon/Sphere 贴图不在这次平面与投影修复范围内。

## DemoGame05 镜面和局部光源投影

- 普通贴图平面采用与 PC 相同的 XY 默认朝向和双面绘制，水面继续位于 XZ 平面。没有贴图的平面也能加载。
- 镜面按实体变换反射相机的位置、目标和上方向，使用原生 Canvas 尺寸的反射纹理。正反两侧均保留原相机所在侧的几何，镜面自身不进入反射通道，避免递归反射。
- 点光源和射灯使用与 PC 相同的 4096×4096、4×4 阴影图集，最多容纳两个点光源的六面投影和四个射灯投影。近远裁剪、接收面法线偏移、透视深度偏差及场景阴影强度与 PC 对齐；每盏灯的阴影只影响对应的直射光。
- 静态投影在相机移动时复用。光源投影矩阵、模型变形或模型变换变化时重新绘制；反射纹理随相机更新，并在尺寸变化或移除镜面时释放。

验证：`dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests -- --surfaces` 和 `node --test tests/web-renderer.test.mjs tests/web-runtime-input.test.mjs`。真实浏览器像素验证使用 `tests/helpers/web-surface-gpu-fixture.mjs` 和 C# 测试生成的 `temp/web-debug/surface-frame.json`，覆盖点光源、射灯、缓存、阴影关闭、镜面正反两侧及镜后裁剪。

## 已有浏览器缓存的升级

旧 WebGL 模块会忽略新 WASM 传入的反射和局部阴影字段。因此，竖直平面、双面标志和光源配置可以正确加载，但画面仍是普通贴图平面，也没有局部光源的遮挡阴影。全新浏览器验证无法覆盖这种新旧模块混用。

- JS 文件的 SHA-256 摘要在构建时写入程序集元数据；JS 或 C# 任一变化都会更新程序集模块 ID。所有 C# interop 模块和 JS 内部依赖统一使用该 ID 的版本 URL，音频模块也保持同一实例。
- PowerShell 和 Bash 预览脚本使用禁止缓存的静态服务器，条件请求也返回当前文件。
- `tests/helpers/web-cache-upgrade-fixture.mjs` 在保留旧模块的同一浏览器中升级播放器，分别关闭镜面、点光源阴影、射灯阴影并比较像素。旧模块的三项差异均为零；升级后要求三项效果都产生实际像素变化，并且阴影区域变暗。

DemoGame05 Release AOT 实测：旧模块的三项差异均为 0；保留浏览器缓存、刷新到新版后，点光源阴影使 59,374 个像素变暗，射灯阴影使 25,305 个像素变暗，镜面改变 56,061 个像素，WebGL 错误为 0。原预览地址 `http://localhost:5052/` 也完成了反射和人物投影的画面检查。

## 替换 web-game 项目内容

可以将另一次完整 Web 导出的内容放到 `artifacts/web-player/wwwroot/web-game`，其中必须包含对应的 `web.manifest.json`、工程、场景、资源和预编译脚本。播放器与游戏内容可以分别更新。

不同游戏常使用相同的 `assets/models/Body/Body.pmx`、贴图和脚本路径。HTTP 资源加载现在以清单中的 SHA-256 作为 URL 版本，清单则每次重新请求；旧版无哈希清单使用每次加载的新版本号。IndexedDB 包装器重新加载清单时会清除已解析的贴图 URI。清单仍保留 Service Worker 离线回退，资源大小和 SHA-256 校验继续生效。

验证：`dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests -- --assets`、`node --test tests/web-service-worker.test.mjs`，以及同一浏览器保留旧 Body.pmx 的实际导出项目加载测试。DemoGame01 的清单和磁盘模型均为 7,481,010 字节；旧缓存模型为 7,468,876 字节。

Release AOT 浏览器实测完整复现原大小不匹配；同一浏览器刷新新版后，通过 `Body.pmx?v=074a9cdca7405d77d34b7ea809745c185e5587427df37530c78c3d6671269594` 读取到 7,481,010 字节，DemoGame01 成功加载 3 个 PMX 模型、52,521 个顶点。发布播放器后仍保留用户替换的游戏内容。
