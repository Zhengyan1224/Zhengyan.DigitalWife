# 三端阴影过滤与 Android 反射画质

DemoGame05 的人物配置为 `receiveShadowMode: toon`，场景为 4× MSAA。Toon 明暗层级仍按工程设置处理；本次对齐的是阴影覆盖率的采样和边缘精度。

| 项目 | 原差异 | 修复 |
| --- | --- | --- |
| OpenGL/GLES 阴影 | 最近邻深度比较，PCF 仍有离散台阶 | 深度比较纹理采用线性过滤，与 Web 的比较采样一致 |
| Vulkan 阴影 | 最近邻读取后逐点比较，局部 PCF 覆盖率只有少量层级 | 对相邻四个深度先比较再插值，方向光和局部光源均应用 |
| Android 主画面 | `auto` 将工程 4× MSAA 降为 1× | `auto`/`high` 使用工程请求值，设备能力不足时仍按后端正常回退 |
| Android 镜面与水面反射 | 默认最长边被限制为 1024 | `auto`/`high` 使用原生相机视口尺寸，与 PC 的默认规则一致 |

PC 与 Android 的反射目标本身均为单采样。本次没有为其中一端额外引入不同的反射 MSAA；主画面和反射纹理分辨率的默认降级已取消。显式低/中质量档、反射数量和渲染目标内存预算继续生效。Android 阴影分辨率仍受工程质量上限约束。

真实 Vulkan GPU 验证使用 4×4 深度图，在连续 32 个像素上对比与 Web 线性比较采样等价的预期值，覆盖方向光、点光源、平滑及 Toon 模式。平滑模式获得 32 个不同覆盖层级，逐像素误差小于 3/255；Toon 风格保持。PMX 多帧绘制、反射/深度和水面/粒子/天空反射通道的已有回归也通过。

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test 'shadow filtering'
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test 'reflection sizes'
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test 'Vulkan PMX draw'
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test 'Vulkan scene upload'
```

本机没有连接 Android 设备。已构建 PC Release 和 ARM64 签名 APK，手机上的实际画面及性能需要安装后复测。
