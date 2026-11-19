# Edge 麦克风采集诊断

2026-11-19 在 DemoGame08 的 Web 桌面精灵测试中，页面返回 `NotReadableError / Could not start audio source`。这条错误与站点权限拒绝不同：浏览器可枚举输入设备，却不能启动采集。

## 已确认的结果

- `http://127.0.0.1:5052` 的 `isSecureContext` 为 true。
- Edge 站点麦克风权限为 granted。
- 当前 Windows 用户的麦克风及 NonPackaged 桌面应用权限均为 Allow。
- 浏览器的三个输入条目是同一个 Realtek 麦克风的默认、通信和实体入口。
- 用户确认 Windows 输入音量测试能检测声音，关闭 PC 游戏后 Web 仍失败。
- 独立 Edge 配置中，在只显示 CSS 的同源页面测试也失败，排除了游戏 ASR 同时采集的干扰。
- 开关回声消除、降噪、自动增益，以及在独立诊断进程中关闭音频服务沙箱，均未解决失败。

原生 Edge 日志定位到：

```text
IAudioClient::Initialize
HRESULT: 0x800706CC
message: 终结点重复。
result: AUDIO_CLIENT_INIT_FAILED
```

请求的原生设备格式是 48 kHz、双声道、32 位浮点。这是 Windows WASAPI 音频客户端初始化失败；音量指示正常不等于浏览器成功建立录音流。重试网页按钮无法修复原生终结点初始化错误。

已验证的引擎修复包括：采集授权流直接交给本地 ASR，避免立即关闭再打开；默认输入失败时尝试实体设备；提供输入设备选择；错误后等待用户操作并恢复原监听会话；退出和取消会释放采集流。

诊断命令：

```powershell
node tests/web-desktop-sprite-browser.mjs --probe-microphone
```

该命令使用独立测试配置、读取原生音频日志，不保存音频。`--audio-sandbox-test` 仅供隔离诊断，不修改用户浏览器配置，也不作为发布运行条件。

遇到相同 WASAPI 初始化错误时，可以在允许短暂中断系统声音的情况下重启 Windows Audio 服务，并重建 AudioEndpointBuilder 服务；操作后应重新验证实际 `getUserMedia`，不能仅依据服务 Running 状态宣布采集已恢复。若原生错误持续存在，需要从 Windows 音频终结点/Realtek 驱动继续处理。

本次实际执行了 Windows Audio 重启、AudioEndpointBuilder 重建，以及已确认的 Realtek 麦克风终结点禁用再启用。每步之后均用独立 Edge 进程重测，错误仍是 `0x800706CC`；音频服务已恢复 Running，麦克风设备状态为 OK。当前真实设备采集尚未恢复。下一步需要保存工作后重启 Windows 再验证，若仍失败则继续检查 Realtek 驱动；不能将引擎回归通过等同于实际麦克风已修复。
