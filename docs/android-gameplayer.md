# Android GamePlayer

首次配置 Android SDK、JDK、`.NET Android` workload，或需要安装 APK/加载 `.dwgame` 的完整
新手步骤，请先阅读 [Android GamePlayer README](../src/Zhengyan.DigitalWife.GamePlayer.Android/README.md)。

The Android GamePlayer is implemented as a separate native host for the existing game-project format. It does not emulate the desktop window host.

## Supported scope

- Game projects and `.dwgame` packages remain the content format.
- C# is the only Android script language. Android publishing will precompile scripts instead of starting Roslyn or Python on the device.
- Desktop sprite mode, transparent click-through windows, window dragging and system-tray integration are not available on Android.
- OpenGL ES and Vulkan are the planned graphics backends. `Auto` prefers Vulkan when the device and all required features are available, then falls back to OpenGL ES.
- OpenCL is never used on Android. Vulkan Compute may be used with the Vulkan backend.

The GameEditor `Package / Publish` panel contains an **Android compatibility check**. It scans every scene, rejects enabled non-C# scripts, and reports desktop-only settings that Android will ignore.

## Host architecture

The Android application uses an `Activity`, `SurfaceView`, `Choreographer` frame scheduling and an EGL window surface. The initial host creates a real OpenGL ES context, responds to Android pause/resume and surface recreation, and presents frames. The desktop `Silk.NET.Windowing` host remains unchanged. Shared engine work is being separated behind platform services for:

- render-surface creation and presentation;
- lifecycle and frame scheduling;
- touch, keyboard and gamepad input;
- audio playback and capture;
- project/package storage.

The host accepts a local directory or `.dwgame` path through the `zhengyan.project_path` Intent extra, a `file://`/`content://` Intent URI, or `files/GameProject` inside the app sandbox. It loads `game.project.json`, the editor scene list and Android compatibility results before creating the render surface. The GLES host now parses `pmx_model` entities through the dependency-free `Zhengyan.DigitalWife.Mmd.Core` assembly and draws GLES3 vertex/index buffers with entity transforms, the selected scene camera, ambient light and directional light. PMX material diffuse colors, UVs and Android-decodable base textures are supported. Missing or unsupported textures fall back to the material color and are reported through logcat.

The PMX GLES path now reads all configured VMD motion layers, blends bone and morph tracks by layer weight using the desktop destination-key Bezier convention, applies group/position/UV/bone morphs, append transforms and CCD IK, and supports playback speed and looping. BDEF1/2/4 models with no animated morph track use GLES3 vertex-shader GPU skinning with a CPU fallback for SDEF/QDEF, large skeletons and animated morph meshes. PMX base, sphere and separate/common toon textures are supported; common toon textures use an Android-generated ramp when the desktop engine resource is not packaged.

Android PMX physics uses BulletSharp and the Android `libbulletc` ABI. It creates PMX sphere/box/capsule bodies, mass/inertia, damping, restitution and friction, applies PMX collision groups and masks, creates 6DoF spring joints, and writes Dynamic/DynamicAndBoneMerge results back into the bone hierarchy using the same offset and Z-axis conversion as the desktop runtime. `EnablePhysics`, project gravity and loop reset settings are honored. If Bullet cannot initialize on an unsupported device ABI, the deterministic lightweight secondary-motion solver remains available as a fallback. The native package is pinned to `Evergine.LibBulletc.Natives` 2025.8.29.27 because it satisfies Android 16's 16 KB page-size requirement.

Android references `Zhengyan.DigitalWife.GameProjects.Core`, which contains only the project model, scene JSON, package reader and compatibility checks. Desktop-only particle mappings and native rendering/audio dependencies are deliberately excluded from this assembly.

## Implementation stages

1. Android lifecycle, render surface and touch input. **Complete.** The host exposes an immutable per-frame multi-touch snapshot with pixel and normalized coordinates, deltas, pressure and touch phases.
2. OpenGL ES PMX scene rendering. **In progress.** PMX animation/material coverage, IK, append, morphs, Bullet rigid bodies/joints/collision and the GPU BDEF path are connected; shadows, particles, post-processing and audio remain.
3. Vulkan surface/swapchain and `Auto` fallback.
4. VMD, lighting, shadows, particles, water, post-processing and GUI parity.
5. Android audio, soft keyboard and gamepad support.
6. GameEditor Android publisher and publish-time C# script compilation.

Build the Android host separately so desktop-only builds do not require the Android workload:

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$jdk = "$env:LOCALAPPDATA\Android\jdk"
dotnet build Zhengyan.DigitalWife.Android.slnx `
  -p:AndroidSdkDirectory=$sdk `
  -p:JavaSdkDirectory=$jdk
```

The Android .NET workload is required to build this solution. The main desktop solution intentionally does not include the Android application project.
