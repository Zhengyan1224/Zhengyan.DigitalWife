# Web Runtime 阶段 3: shared scene runtime and PMX/VMD pipeline [已实现]

> Status: **已实现**
>
> Date: 2026-10-31
>
> Design: `docs/web-runtime-design.md`, 阶段 3

## 1. 本阶段交付

阶段 3 turns the WebGL2 prototype into a real Web game runtime. The browser reads `web-game/` static assets, project/scene JSON, PMX, VMD, textures and precompiled scripts. .NET WebAssembly reuses `RuntimeScene`, `PmxPoseEvaluator` and `RuntimeCameraInput`; it does not duplicate platform-specific scene or animation algorithms.

## 2. 已实现能力

- Assets and scenes: `WebHttpAssetStore`, manifest/hash validation, `WebProjectLoader`, VMD loading, PMX loading and `WebLoadProgress`.
- PMX/VMD: shared `PmxPoseEvaluator` covers BDEF1/BDEF2/BDEF4/SDEF/QDEF, morphs, bone morphs, append bones, IK, VMD bone/morph keys and lightweight physics fallback.
- Rendering: one GPU mesh per PMX entity, entity transform and visibility flags, dynamic vertex buffers, PMX material ranges, diffuse/ambient color, image textures through `fetch` + `createImageBitmap`, transparency, double-sided materials, edge pass and a directional depth shadow pass.
- Input: Pointer, Touch, Wheel and Keyboard map to `RuntimeCameraInput`; WASD/arrow keys move the camera, pointer/touch rotates and pans, wheel zooms.
- Scripts: `IWebGameScript`, `WebScriptContext` and `WebScriptRuntime` load precompiled Web assemblies, validate SHA/ABI, call `Start`/`Update` and keep diagnostics visible.
- Publishing: `WebGamePackageExporter` produces a static Web directory with a manifest and project-relative paths.
- DemoGame04: real export smoke test passed for `D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame04` (69 assets, about 32 MB, no Windows drive path in the manifest).

## 3. Runtime flow

1. Create WebGL2 and install Pointer/Touch/Wheel/Keyboard events.
2. Read `web.manifest.json`, project, scene and report progress.
3. Load scene VMD data and create shared `RuntimeScene`.
4. Load each PMX entity, its MotionLayers and texture references, then create `PmxPoseEvaluator` and one WebGL2 mesh resource per entity.
5. Load and start precompiled Web scripts.
6. Each frame updates the scene and scripts, evaluates every PMX pose, uploads changed vertex data by entity id, applies each entity transform, and renders shadow and material passes.
7. Stop/context loss disposes scenes, scripts and GPU resources.

## 4. Validation

Passed checks:

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj --no-restore -p:NuGetAudit=false -v minimal
dotnet build src/Zhengyan.DigitalWife.GameProjects.Core/Zhengyan.DigitalWife.GameProjects.Core.csproj --no-restore -p:NuGetAudit=false -v minimal
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests --no-restore
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests --no-restore -- --export-web D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame04 <temporary-output>
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js
node tests/web-runtime-smoke.mjs
```

The self-tests cover shared pose output, `MemoryAssetStore` and the Web exporter. The fixture remains 4 vertices, 2 triangles, 1 material and 1 bone. DemoGame04 export verified copying and path cleanup.

The current machine has no Chrome, Edge or Chromium executable, so final GPU/driver/touch manual verification must run on a browser-equipped machine. Code, publish assets, module syntax and shared runtime checks are complete.

## 5. Next recovery entry

阶段 3 is complete. Continue `docs/web-runtime-phase4.md` for optional `.dwgame` in-memory loading, IndexedDB cache and incremental/CDN downloads. Keep `IGameAssetStore`, manifest diagnostics, PMX pose, WebGL2 materials/shadows and input ABI unchanged.
