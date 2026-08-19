# Android RenderTexture API

Android GamePlayer 的 C# 脚本通过 `Services` 访问当前场景的 RenderTexture：

```csharp
var targets = Services.GetRenderTextures();
var target = Services.GetRenderTexture("CameraTarget");
Services.ConfigureRenderTexture("CameraTarget", "manual");
Services.RefreshRenderTexture("CameraTarget");
```

支持的刷新模式为 `every_frame`、`interval`/`timed` 和 `manual`。`GetRenderTexture` 返回
`Id`、`Name`、`Width`、`Height`、`RefreshMode`、`RefreshIntervalSeconds`、`HasRendered` 和
`LastRenderedSeconds`。RenderTexture 在场景中仍通过 `rt:<id-or-name>` 作为 Plane 或材质纹理路径采样。

Android GLES 自定义 shader 必须使用 `#version 300 es`、`void main()` 和引擎约定的 uniform；
不支持 `layout(binding=...)`。发布前可以执行：

```powershell
pwsh tools/Validate-AndroidGlesShader.ps1 -Vertex shaders/custom.vert -Fragment shaders/custom.frag
```
