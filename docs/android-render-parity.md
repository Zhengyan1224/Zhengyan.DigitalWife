# Android runtime and rendering parity

The active GLES and Vulkan hosts both use `AndroidSceneGame` and the shared
`Mmd.Game` components. The older `AndroidPmxSceneRenderer` is not the basis for
the current parity assessment. Shared code establishes implementation coverage;
it does not establish identical output on every Android GPU.

## Current implementation

| Feature | Android GLES | Android Vulkan | Scope |
| --- | --- | --- | --- |
| PMX materials, edges, particles, water, planes, skybox | Shared components | Shared components | Driver output still needs device comparison |
| Directional and local shadows, reflections, render textures | Shared pass abstractions | Shared pass abstractions | Android quality budgets still apply |
| Custom PMX shaders, uniforms and material texture overrides | Wired through `PmxModelComponent` | Wired through `PmxModelComponent` | GLES shader source and Vulkan SPIR-V have different contracts; test each asset on both backends |
| Scene loading screen | Shared desktop `LoadingScreenComponent` | Same component, Vulkan renderer | Background color/image/opacity and full progress-bar layout, border and padding |
| C# loading scripts | Implemented | Implemented | `loading_started`, `loading_progress`, `loading_completed`; Android also reports `loading_failed` |
| C# publication | Shared typed Android API and compiler | Same | Editor emits `compiled/android/manifest.json` and DLLs; player consumes the same globals type |
| GUI button events and default targets | Desktop event order and target selection | Same helper | `pressed`, `released`, then configured click event; untargeted GUI events select one scripted entity |
| LLM streaming and tool calls | Shared desktop `RuntimeLlm` | Same | Multi-round calls, script tool callbacks, native-tool/text-protocol fallback, cancellation and round limits |
| Skills and memory | Shared desktop `RuntimeLlmSkillTools` | Same | Controlled by `Project.Llm.EnableSkills` / `EnableMemory`; memory uses `Save.SaveDirectory/memory` |
| Scene audio | Android `MediaPlayer` | Same | Preloaded during scene loading; play/pause/stop/loop/volume supported |
| Spatial audio mixing | Deferred | Deferred | Desktop's OpenAL source/listener features need an Android native backend and shared scene/script configuration |
| OpenCL | Not enabled | Not enabled | GLES uses CPU; Vulkan can use configured Vulkan Compute with CPU fallback |
| Python, desktop window control, desktop sprite windows | Outside Android scope | Same | No implementation added |

## Loading and callbacks

Loading displays initial frames before resource work, then advances one resource
step per frame. Entity `Start` and `Update` are gated until resource loading and
the loading-script completion event finish. A failed step remains failed and
does not become a successful completion on the next frame. Scene changes and
surface recreation reset the loading session and cancel old LLM requests.

The native Activity overlay covers package extraction and initialization before
the game can draw, and displays errors. Once a game frame is available, the
shared renderer displays the scene's loading screen. Native game GUI is hidden
until the scene is ready.

LLM notifications retain separate event names and callback names. `LlmDelta` is
the new text; `LlmText` is accumulated text. Named script tools execute through
the render-thread dispatcher and return their result to the model with the
original tool-call ID, following the existing desktop protocol. See the
[OpenAI function calling guide](https://developers.openai.com/api/docs/guides/function-calling)
for the underlying Chat Completions exchange.

Android Skills commands use `/system/bin/sh` and the app's filesystem permissions.
Desktop executables and Python installations are not supplied. Android ASR uses
the device speech recognizer: the exported wake-word API filters recognized text,
but desktop capture chunk settings cannot be reproduced exactly by that service.
TTS completion callbacks wait for the Android utterance completion notification.

Game packages can configure HTTP or HTTPS service URLs at runtime. The Android
manifest and network security configuration permit HTTP, matching desktop
connectivity; HTTPS continues to use the platform's default certificate trust.
LLM request start, endpoint, completion, cancellation, tool fallback and failures
are written to logcat with the `ZhengyanGamePlayer` tag. Failures include the
exception stack and inner network exceptions; API keys are not logged.

```powershell
adb logcat -s ZhengyanGamePlayer
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Android LLM"
```

## Verification

### GUI buttons

Both Android hosts preserve touch down/up ordering, including taps completed
between frames. Buttons send `pressed` immediately and `released` to the original
button when the touch ends or is canceled. A click is sent only when the same
button receives the release; blank event names default to `clicked`.

GUI events resolve an explicit target by ID or name. Empty or missing targets
use the desktop fallback: prefer a scripted PMX model that other PMX models bind
to, then an independent scripted PMX model, then sort by name. Without a scripted
PMX model, use the first scripted entity. GUI events with no eligible target are
ignored. In DemoGame01 scene2, this selects `Body` and prevents the house and
outfit scripts from speaking in response to the recording button.

Run the button, recording, target, cancellation and quick-tap regression cases:

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Android GUI"
```

### Motion loops, physics timing and Vulkan uploads

Android applies the scene's global loop-reset default only when initializing a
PMX component. Runtime per-layer settings (including mixed `true`/`false` layers)
survive scene synchronization; explicit script changes to the global setting
still update all layers.

Time-synchronized PMX playback now advances animation and physics together in
steps of at most 1/30 second on slow frames, then skins/uploads the final pose
once. It does not drop physics time to preserve frame rate. Normal frame deltas
and the intentional slowdown of frame-rate-dependent playback remain unchanged.
This implementation is shared by desktop, Android GLES and Android Vulkan.

Android Vulkan relies on frame-slot fences and GPU barriers instead of waiting
for the immediately preceding graphics frame before CPU updates. Device-local
CPU uploads use three fenced staging slots, including GPU-skinning fallback.

The package password editor uses the native editable connection and permits
dialog IME focus. For the reported Redmi Note 12 Pro secure-keyboard failure,
it requests the regular password keyboard via `EditorInfo` while keeping the
actual TextView in masked password mode. Suggestions and personalized learning
are disabled. Window focus only shows the keyboard; it no longer restarts an
input session that Android just established. Real keyboard typing, deletion,
paste, Done, retry and cancellation still require device validation.

The runtime suite exercises slow frames, loop flags, pause and asynchronous
Vulkan compute/CPU uploads. A read-only native Bullet comparison can also use a
project's actual character and motion, comparing 32 FPS with slow frames (1/32
second is exactly representable, so Bullet receives identical substep timing):

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --physics-project ../GameEditorProjects/DemoGame07 assets/motions/Lamb.vmd
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --skinning-project ../GameEditorProjects/DemoGame07 assets/motions/Lamb.vmd
```

The skinning probe also compares CPU and Vulkan Compute vertex positions using
the same animation/physics timing. It performs GPU readback for verification;
the player keeps its asynchronous Vulkan path. Device frame pacing can still
affect the simulated motion even when these equal-timestep results agree.

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --compile-project <project-directory>
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
```

For a standalone Debug APK, build the Android project with
`-p:EmbedAssembliesIntoApk=true` and the appropriate JDK 21 / Android SDK paths.
The default fast-deployment APK relies on assemblies supplied by development tools.

The runtime regression suite covers published script execution, lambda callbacks,
loading failures, streamed tool rounds, script-thread dispatch, protocol fallback,
cancellation (including child processes launched by Skills), Skills/memory paths, encrypted package round trips, and consecutive
desktop/Android exports in one process. LLM tests use an in-memory HTTP handler;
no service credentials or paid requests are required.

The DemoGame01 regression compiles all 11 bound C# scripts unchanged. GameEditor,
PC GamePlayer, and the Android APK were built with .NET 10; Android requires JDK 21.
The APK build reports existing native dependency warnings for 16 KB page alignment
and duplicate SDL libraries. No Android device was connected for this validation.

Device regression is still required for GLES/Vulkan loading screens, custom
shaders, rendering output, native speech services, background/resume behavior,
and Android 16 KB page devices. Re-export packages and update the Android player
together when adopting the new typed scripting assembly.
