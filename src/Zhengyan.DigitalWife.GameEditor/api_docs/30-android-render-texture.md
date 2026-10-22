---
id: 30-android-render-texture
title: Android RenderTexture / GLES Shader
category: 渲染
objects:
  - Services
  - AndroidGlesShaderContract
keywords:
  - Android
  - RenderTexture
  - GLES
  - shader
---

# Android RenderTexture API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Android RenderTexture / GLES Shader |
| 分类 | 渲染 |
| 主要对象 | `Services`, `AndroidGlesShaderContract` |
| C# 入口 | `Services.GetRenderTextures, Services.ConfigureRenderTexture, Services.RefreshRenderTexture` |
| Python 入口 | Android 不支持 Python 脚本。 |
| 说明 | Android RenderTexture 查询与刷新，以及 GLES 自定义 shader 约定。 |

## API 内容

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
