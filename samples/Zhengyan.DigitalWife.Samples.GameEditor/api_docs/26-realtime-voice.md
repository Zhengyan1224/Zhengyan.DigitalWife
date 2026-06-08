---
id: realtime-voice
title: Realtime Voice API
category: 语音
objects:
  - RuntimeRealtimeVoice
  - RuntimeRealtimeVoiceScriptEvent
  - scene.realtime_voice
keywords:
  - realtime voice
  - wake word
  - transcription
  - speech
---

# Realtime Voice API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Realtime Voice API |
| 分类 | 语音 |
| 主要对象 | ``RuntimeRealtimeVoice``, ``RuntimeRealtimeVoiceScriptEvent``, ``scene.realtime_voice`` |
| C# 入口 | `Scene.RealtimeVoice.Start*` |
| Python 入口 | `scene.realtime_voice.start_*` |
| 说明 | 实时语音唤醒、转写、响应、语音播放、后台回调和事件字段。 |

## API 内容

GamePlayer 支持从脚本调用 `Zhengyan.DigitalWife.Samples.RealtimeVoice` 服务。配置在 GameEditor 的 `Project` 标签页 `Realtime Voice` 中，也会保存到 `game.project.json` 的 `realtimeVoice` 节点。

这组 API 面向远端语音服务，主要能力是：

- 本地麦克风录音并上传给服务转写
- 按配置的唤醒词组监听唤醒词，并通过脚本事件通知
- 发送用户文本到 Realtime 会话，流式播放返回音频，并把 transcript delta / completed 回调给脚本
- 调用 `/v1/audio/speech` 做固定文本提示语

项目配置重点：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本层 Realtime Voice。 |
| `BaseUrl` | 服务根地址，例如 `http://127.0.0.1:5000`。 |
| `RealtimePath` | Realtime WebSocket 路径，默认 `/v1/realtime`。 |
| `AudioSpeechPath` | 文本直出 TTS 路径，默认 `/v1/audio/speech`。 |
| `ApiKeyEnvironmentVariable` / `ApiKey` | API Key 优先读取直接配置，其次读环境变量。 |
| `Model` | 会话默认模型名。 |
| `Voice` | 远端 voice 字段。 |
| `InputAudioSampleRate` | 发给 Realtime 协议层的输入采样率。 |
| `OutputAudioSampleRate` | 期望远端返回的输出采样率。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空时使用默认输入设备。 |
| `OutputVolume` | 回放远端语音时的输出音量倍数。 |
| `PromptSpeed` | `/v1/audio/speech` 固定提示语请求的默认速度。 |
| `UserCapture` | `start_transcription` / `StartVoiceTurn` 使用的本地录音参数。 |
| `WakeWord.Enabled` / `WakeWord.Keywords` | 是否启用脚本唤醒词监听以及唤醒词组。 |
| `WakeWord.Capture` | 唤醒词分片录音参数。 |

### C# Realtime Voice

`Scene.RealtimeVoice` 采用后台任务 + 脚本事件回调的模式。不要在 `Update` 里每帧调用 `Start*`；通常在 `IsStart`、GUI 点击、或收到上一轮回调后再触发下一步。

C# API：

| API | 说明 |
| --- | --- |
| `Scene.RealtimeVoice.Enabled` | 当前项目是否启用 Realtime Voice。 |
| `Scene.RealtimeVoice.BaseUrl` | 服务根地址。 |
| `Scene.RealtimeVoice.Model` | 默认模型名。 |
| `Scene.RealtimeVoice.Voice` | 默认 voice。 |
| `Scene.RealtimeVoice.WakeWordEnabled` | 是否启用了唤醒词监听配置。 |
| `Scene.RealtimeVoice.WakeWords` | 当前配置的唤醒词列表。 |
| `Scene.RealtimeVoice.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.RealtimeVoice.IsWakeWordMonitoring` | 当前是否在监听唤醒词。 |
| `StartWakeWordMonitoring(entity, onDetectedCallback, onErrorCallback)` | 开始后台唤醒词监听。 |
| `StopWakeWordMonitoring()` | 停止唤醒词监听。 |
| `StartTranscription(entity, requestId, timeoutSeconds, onCompletedCallback, onTimeoutCallback, onErrorCallback)` | 录音直到静音并转写；可配置超时。 |
| `StartResponse(entity, userText, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback)` | 发送用户文本到 Realtime 会话，流式播放远端音频。 |
| `StartVoiceTurn(entity, requestId, timeoutSeconds, onTranscriptionCompletedCallback, onDeltaCallback, onCompletedCallback, onTimeoutCallback, onErrorCallback)` | 录音、转写、发起回复的一体化后台流程；可配置等待用户输入的超时。 |
| `StartSpeakText(entity, text, speed, requestId, onCompletedCallback, onErrorCallback)` | 调用 `/v1/audio/speech` 播放固定提示语。 |
| `ResetConversationAsync()` | 清空远端会话中的历史消息。 |
| `CancelRequest(requestId)` | 取消指定后台语音请求。 |
| `CancelAllRequests()` | 取消当前实体触发的全部后台语音请求。 |

典型流程：开始唤醒词监听，命中后发起一轮语音对话。

```csharp
if (IsStart && Scene.RealtimeVoice.Enabled)
{
    Scene.RealtimeVoice.StartWakeWordMonitoring(
        Entity,
        onDetectedCallback: "wake_word_hit",
        onErrorCallback: "wake_word_error");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "wake_word_hit")
{
    Scene.GetGuiControl("Status")?.SetValue($"已唤醒: {RealtimeVoiceWakeWord}");
    Scene.RealtimeVoice.StartVoiceTurn(
        Entity,
        onTranscriptionCompletedCallback: "voice_transcribed",
        onDeltaCallback: "voice_delta",
        onCompletedCallback: "voice_done",
        onErrorCallback: "voice_error");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_transcribed")
{
    Scene.GetGuiControl("Heard")?.SetValue(RealtimeVoiceText);
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_delta")
{
    Scene.GetGuiControl("Reply")?.SetValue(RealtimeVoiceAccumulatedText);
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_done")
{
    Scene.GetGuiControl("Reply")?.SetValue(RealtimeVoiceText);
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_error")
{
    Console.Error.WriteLine(RealtimeVoiceError);
}
```

完整的“唤醒词 -> 远端转写/对话 -> TTS 播放 -> 30s 内继续对话，超时后回到待机”脚本示例：

说明：

- 现在脚本层已经支持 `timeout` 和 `cancel`，可以完整实现接近 `DigitalHuman` 的状态机。
- 下面的写法是：先靠唤醒词进入会话；用户说完后，数字人回复；回复完成后继续等待 30 秒下一句；如果 30 秒内没有新的语音输入，则清空会话并回到“等待唤醒”。
- 假设项目里已经有两个动作文件：`assets/motions/basic_stand.vmd` 和 `assets/motions/basic_wait.vmd`。
- 这里用 `basic_stand.vmd` 和 `basic_wait.vmd` 同时放进动作层列表，通过层权重平滑过渡，效果更接近 `DigitalHuman` 项目的动作混合方式。

```csharp
static class VoiceState
{
    public static bool InConversation;
    public static bool WakeWordMonitorStarted;
    public static bool WaitingForUserSpeech;
    public static string PendingTurnRequestId = string.Empty;
    public static string LastAssistantReply = string.Empty;
    public static DateTimeOffset WaitTurnExpiresAtUtc = DateTimeOffset.MinValue;
    public static bool MotionBlendReady;
    public static float WaitLayerWeight;
    public static float TargetWaitLayerWeight;
}

const string StandMotion = "assets/motions/basic_stand.vmd";
const string WaitMotion = "assets/motions/basic_wait.vmd";
const float MotionBlendDurationSeconds = 0.35f;
const double WaitForUserSpeechTimeoutSeconds = 30.0;

void EnsureMotionBlendSetup()
{
    if (VoiceState.MotionBlendReady)
    {
        return;
    }

    Entity.SetMotionLayers(new[]
    {
        new MotionLayerDefinition(StandMotion, 1.0f, false),
        new MotionLayerDefinition(WaitMotion, 0.0f, false)
    });
    Entity.LoopMotion = true;
    Entity.PlayMotion();
    VoiceState.MotionBlendReady = true;
    VoiceState.WaitLayerWeight = 0.0f;
    VoiceState.TargetWaitLayerWeight = 0.0f;
}

void SetStandState()
{
    EnsureMotionBlendSetup();
    VoiceState.TargetWaitLayerWeight = 0.0f;
}

void SetWaitState()
{
    EnsureMotionBlendSetup();
    VoiceState.TargetWaitLayerWeight = 1.0f;
}

void UpdateMotionBlend()
{
    if (!IsUpdate || !VoiceState.MotionBlendReady)
    {
        return;
    }

    float current = VoiceState.WaitLayerWeight;
    float target = VoiceState.TargetWaitLayerWeight;
    if (MathF.Abs(current - target) < 0.001f)
    {
        return;
    }

    float step = (float)(DeltaSeconds / MotionBlendDurationSeconds);
    float next = target > current
        ? MathF.Min(target, current + step)
        : MathF.Max(target, current - step);

    VoiceState.WaitLayerWeight = next;
    Entity.SetMotionLayerWeight(StandMotion, 1.0f - next);
    Entity.SetMotionLayerWeight(WaitMotion, next);
}

void BeginWaitingForUserSpeech()
{
    SetWaitState();
    VoiceState.WaitingForUserSpeech = true;
    VoiceState.WaitTurnExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(WaitForUserSpeechTimeoutSeconds);
    Scene.GetGuiControl("Status")?.SetValue($"等待下一句（{WaitForUserSpeechTimeoutSeconds:0}s）");
    VoiceState.PendingTurnRequestId = Scene.RealtimeVoice.StartVoiceTurn(
        Entity,
        timeoutSeconds: (float)WaitForUserSpeechTimeoutSeconds,
        onTranscriptionCompletedCallback: "voice_transcribed",
        onDeltaCallback: "voice_delta",
        onCompletedCallback: "voice_done",
        onTimeoutCallback: "voice_timeout",
        onErrorCallback: "voice_error");
}

void EndConversationAndReturnToStand(string statusText)
{
    SetStandState();
    VoiceState.WaitingForUserSpeech = false;
    VoiceState.WaitTurnExpiresAtUtc = DateTimeOffset.MinValue;

    if (!string.IsNullOrWhiteSpace(VoiceState.PendingTurnRequestId))
    {
        Scene.RealtimeVoice.CancelRequest(VoiceState.PendingTurnRequestId);
        VoiceState.PendingTurnRequestId = string.Empty;
    }

    VoiceState.InConversation = false;
    _ = Scene.RealtimeVoice.ResetConversationAsync();
    Scene.GetGuiControl("Status")?.SetValue(statusText);
    EnsureWakeWordMonitor();
}

void EnsureWakeWordMonitor()
{
    EnsureMotionBlendSetup();
    if (VoiceState.WakeWordMonitorStarted || !Scene.RealtimeVoice.Enabled)
    {
        return;
    }

    Scene.RealtimeVoice.StartWakeWordMonitoring(
        Entity,
        onDetectedCallback: "wake_word_hit",
        onErrorCallback: "voice_error");
    VoiceState.WakeWordMonitorStarted = true;
    SetStandState();
    Scene.GetGuiControl("Status")?.SetValue("等待唤醒");
}

if (IsStart)
{
    EnsureWakeWordMonitor();
}

if (IsUpdate)
{
    UpdateMotionBlend();

    if (VoiceState.WaitingForUserSpeech
        && VoiceState.WaitTurnExpiresAtUtc != DateTimeOffset.MinValue
        && DateTimeOffset.UtcNow >= VoiceState.WaitTurnExpiresAtUtc)
    {
        EndConversationAndReturnToStand("超时，重新等待唤醒");
    }
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "wake_word_hit")
{
    if (VoiceState.InConversation)
    {
        return;
    }

    VoiceState.InConversation = true;
    Scene.RealtimeVoice.StopWakeWordMonitoring();
    VoiceState.WakeWordMonitorStarted = false;
    SetWaitState();
    Scene.GetGuiControl("Status")?.SetValue($"已唤醒: {RealtimeVoiceWakeWord}");

    if (!string.IsNullOrWhiteSpace(RealtimeVoiceText))
    {
        // 用户在同一句里已经说了“唤醒词 + 问题”，直接发给远端会话。
        Scene.RealtimeVoice.StartResponse(
            Entity,
            RealtimeVoiceText,
            onDeltaCallback: "voice_delta",
            onCompletedCallback: "voice_done",
            onErrorCallback: "voice_error");
    }
    else
    {
        // 用户只说了唤醒词，先播提示语，播完后再录下一句。
        Scene.RealtimeVoice.StartSpeakText(
            Entity,
            "我在，请说。",
            speed: 1.0f,
            onCompletedCallback: "prompt_done",
            onErrorCallback: "voice_error");
    }
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "prompt_done")
{
    Scene.GetGuiControl("Status")?.SetValue("正在听");
    BeginWaitingForUserSpeech();
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_transcribed")
{
    VoiceState.WaitingForUserSpeech = false;
    VoiceState.WaitTurnExpiresAtUtc = DateTimeOffset.MinValue;
    SetWaitState();
    Scene.GetGuiControl("Heard")?.SetValue(RealtimeVoiceText);
    Scene.GetGuiControl("Status")?.SetValue("思考中");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_delta")
{
    SetWaitState();
    VoiceState.LastAssistantReply = RealtimeVoiceAccumulatedText;
    Scene.GetGuiControl("Reply")?.SetValue(VoiceState.LastAssistantReply);
    Scene.GetGuiControl("Status")?.SetValue("正在说话");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_done")
{
    SetWaitState();
    VoiceState.PendingTurnRequestId = string.Empty;
    string finalReply = string.IsNullOrWhiteSpace(VoiceState.LastAssistantReply)
        ? RealtimeVoiceText
        : VoiceState.LastAssistantReply;
    Scene.GetGuiControl("Reply")?.SetValue(finalReply);
    VoiceState.LastAssistantReply = string.Empty;
    BeginWaitingForUserSpeech();
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_timeout")
{
    EndConversationAndReturnToStand("超时，重新等待唤醒");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_error")
{
    Console.Error.WriteLine(RealtimeVoiceError);
    EndConversationAndReturnToStand("语音出错，重新等待唤醒");
}
```

固定提示语示例：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.RealtimeVoice.StartSpeakText(
        Entity,
        "我在，请说。",
        speed: 1.0f,
        onCompletedCallback: "prompt_done",
        onErrorCallback: "prompt_error");
}
```

Realtime Voice 回调事件字段：

| 字段 | 说明 |
| --- | --- |
| `RealtimeVoiceRequestId` | 请求 Id。 |
| `RealtimeVoiceEventName` | 事件名：`wake_word_detected`、`transcription_completed`、`delta`、`completed`、`speech_completed`、`timeout`、`error`。 |
| `RealtimeVoiceText` | 主文本：唤醒词事件通常是去掉唤醒词后的尾文本，转写事件是用户文本，完成事件是最终回复文本。 |
| `RealtimeVoiceDelta` | 当前 transcript 增量。 |
| `RealtimeVoiceAccumulatedText` | 当前累计回复文本。 |
| `RealtimeVoiceIsFinal` | 是否最终事件。 |
| `RealtimeVoiceError` | 错误文本。 |
| `RealtimeVoiceCallbackName` | 当前命中的脚本回调名。 |
| `RealtimeVoiceWakeWord` | 命中的唤醒词。 |
| `RealtimeVoiceRecognizedText` | 原始识别文本。 |

### Python Realtime Voice

`scene.realtime_voice` 提供与 C# 同名的后台能力，但 Python 会自动把回调目标绑定到当前脚本实体。

Python API：

| API | 说明 |
| --- | --- |
| `scene.realtime_voice.enabled` | 当前项目是否启用 Realtime Voice。 |
| `scene.realtime_voice.base_url` | 服务根地址。 |
| `scene.realtime_voice.model` | 默认模型名。 |
| `scene.realtime_voice.voice` | 默认 voice。 |
| `scene.realtime_voice.wake_word_enabled` | 是否启用了唤醒词监听配置。 |
| `scene.realtime_voice.wake_words` | 当前配置的唤醒词列表。 |
| `scene.realtime_voice.input_device_index` | 当前输入设备索引。 |
| `scene.realtime_voice.start_wake_word_monitoring(on_detected="wake_word_detected", on_error="wake_word_error")` | 开始后台唤醒词监听。 |
| `scene.realtime_voice.stop_wake_word_monitoring()` | 停止唤醒词监听。 |
| `scene.realtime_voice.start_transcription(request_id=None, timeout_seconds=None, on_completed="realtime_voice_transcription_completed", on_timeout="realtime_voice_timeout", on_error="realtime_voice_error")` | 录音直到静音并转写；可配置超时。 |
| `scene.realtime_voice.start_response(user_text, request_id=None, on_delta="realtime_voice_delta", on_completed="realtime_voice_completed", on_error="realtime_voice_error")` | 发送用户文本到 Realtime 会话并流式播放回复。 |
| `scene.realtime_voice.start_voice_turn(request_id=None, timeout_seconds=30, on_transcription_completed="realtime_voice_transcription_completed", on_delta="realtime_voice_delta", on_completed="realtime_voice_completed", on_timeout="realtime_voice_timeout", on_error="realtime_voice_error")` | 一体化后台语音对话流程；可配置等待用户输入的超时。 |
| `scene.realtime_voice.start_speak_text(text, speed=None, request_id=None, on_completed="realtime_voice_speech_completed", on_error="realtime_voice_error")` | 文本直出 TTS。 |
| `scene.realtime_voice.reset_conversation()` | 重置远端会话。 |
| `scene.realtime_voice.cancel_request(request_id)` | 取消指定后台语音请求。 |
| `scene.realtime_voice.cancel_all_requests()` | 取消当前脚本实体触发的全部后台语音请求。 |

Python 典型流程：

```python
state = {
    "in_conversation": False,
    "waiting_for_user_speech": False,
    "pending_turn_request_id": "",
    "last_assistant_reply": "",
    "wake_word_monitor_started": False,
    "wait_turn_expires_at": None,
    "motion_blend_ready": False,
    "wait_weight": 0.0,
    "target_wait_weight": 0.0
}

STAND_MOTION = "assets/motions/basic_stand.vmd"
WAIT_MOTION = "assets/motions/basic_wait.vmd"
BLEND_DURATION_SECONDS = 0.35
WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS = 30.0

def ensure_motion_blend_setup(entity):
    if state["motion_blend_ready"]:
        return

    entity.set_motion_layers([
        {"path": STAND_MOTION, "weight": 1.0, "resetPhysicsOnLoop": False},
        {"path": WAIT_MOTION, "weight": 0.0, "resetPhysicsOnLoop": False},
    ])
    entity.set_loop_motion(True)
    entity.play_motion()
    state["motion_blend_ready"] = True
    state["wait_weight"] = 0.0
    state["target_wait_weight"] = 0.0

def set_stand_state(entity):
    ensure_motion_blend_setup(entity)
    state["target_wait_weight"] = 0.0

def set_wait_state(entity):
    ensure_motion_blend_setup(entity)
    state["target_wait_weight"] = 1.0

def update_motion_blend(entity, delta_seconds):
    if not state["motion_blend_ready"]:
        return

    current = state["wait_weight"]
    target = state["target_wait_weight"]
    if abs(current - target) < 0.001:
        return

    step = delta_seconds / BLEND_DURATION_SECONDS
    if target > current:
        current = min(target, current + step)
    else:
        current = max(target, current - step)

    state["wait_weight"] = current
    entity.set_motion_layer_weight(STAND_MOTION, 1.0 - current)
    entity.set_motion_layer_weight(WAIT_MOTION, current)

def begin_waiting_for_user_speech(entity, scene):
    set_wait_state(entity)
    state["waiting_for_user_speech"] = True
    state["wait_turn_expires_at"] = time.time() + WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS
    status = scene.get_gui_control("Status")
    if status:
        status.set_value(f"等待下一句（{WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS:.0f}s）")
    state["pending_turn_request_id"] = scene.realtime_voice.start_voice_turn(
        timeout_seconds=WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS,
        on_transcription_completed="voice_transcribed",
        on_delta="voice_delta",
        on_completed="voice_done",
        on_timeout="voice_timeout",
        on_error="voice_error")

def end_conversation_and_return_to_stand(entity, scene, status_text):
    set_stand_state(entity)
    state["waiting_for_user_speech"] = False
    state["wait_turn_expires_at"] = None

    if state["pending_turn_request_id"]:
        scene.realtime_voice.cancel_request(state["pending_turn_request_id"])
        state["pending_turn_request_id"] = ""

    state["in_conversation"] = False
    scene.realtime_voice.reset_conversation()
    status = scene.get_gui_control("Status")
    if status:
        status.set_value(status_text)
    state["wake_word_monitor_started"] = False
    start(entity, scene, input=None, audio=None)

def start(entity, scene, input, audio):
    if scene.realtime_voice.enabled and not state["wake_word_monitor_started"]:
        set_stand_state(entity)
        scene.realtime_voice.start_wake_word_monitoring(
            on_detected="wake_word_hit",
            on_error="voice_error")
        state["wake_word_monitor_started"] = True

def update(entity, scene, input, audio, delta_seconds):
    update_motion_blend(entity, delta_seconds)
    if state["waiting_for_user_speech"] and state["wait_turn_expires_at"] is not None and time.time() >= state["wait_turn_expires_at"]:
        end_conversation_and_return_to_stand(entity, scene, "超时，重新等待唤醒")

def wake_word_hit(entity, scene, input, audio, event):
    if state["in_conversation"]:
        return

    state["in_conversation"] = True
    scene.realtime_voice.stop_wake_word_monitoring()
    state["wake_word_monitor_started"] = False
    set_wait_state(entity)

    status = scene.get_gui_control("Status")
    if status:
        status.set_value("已唤醒: " + event["wakeWord"])

    if event["text"].strip():
        scene.realtime_voice.start_response(
            event["text"],
            on_delta="voice_delta",
            on_completed="voice_done",
            on_error="voice_error")
    else:
        scene.realtime_voice.start_speak_text(
            "我在，请说。",
            on_completed="prompt_done",
            on_error="voice_error")

def prompt_done(entity, scene, input, audio, event):
    status = scene.get_gui_control("Status")
    if status:
        status.set_value("正在听")
    begin_waiting_for_user_speech(entity, scene)

def voice_transcribed(entity, scene, input, audio, event):
    state["waiting_for_user_speech"] = False
    state["wait_turn_expires_at"] = None
    set_wait_state(entity)
    heard = scene.get_gui_control("Heard")
    if heard:
        heard.set_value(event["text"])

def voice_delta(entity, scene, input, audio, event):
    set_wait_state(entity)
    state["last_assistant_reply"] = event["accumulatedText"]
    reply = scene.get_gui_control("Reply")
    if reply:
        reply.set_value(state["last_assistant_reply"])

def voice_done(entity, scene, input, audio, event):
    set_wait_state(entity)
    state["pending_turn_request_id"] = ""
    reply = scene.get_gui_control("Reply")
    if reply:
        final_reply = state["last_assistant_reply"] or event["text"]
        reply.set_value(final_reply)
    state["last_assistant_reply"] = ""
    begin_waiting_for_user_speech(entity, scene)

def voice_timeout(entity, scene, input, audio, event):
    end_conversation_and_return_to_stand(entity, scene, "超时，重新等待唤醒")

def voice_error(entity, scene, input, audio, event):
    end_conversation_and_return_to_stand(entity, scene, "语音出错，重新等待唤醒")
    print("Realtime Voice error:", event["error"])
```

Python 通用回调事件字典字段包括：

- `requestId`
- `eventName`
- `text`
- `delta`
- `accumulatedText`
- `isFinal`
- `error`
- `callbackName`
- `wakeWord`
- `recognizedText`

注意事项：

- `RealtimeVoice` 的后台方法是非阻塞的，但如果你在 `Update` 每帧都调用 `start_*`，会不断创建新任务。通常要用脚本状态位控制，或只在 GUI 点击、唤醒词命中、上一轮完成回调后再触发下一轮。
- 对“30s 内等待下一句，超时后回到站立”这类逻辑，推荐像上面的示例一样，由脚本自己维护 `WaitTurnExpiresAtUtc` / `wait_turn_expires_at` 并主动 `cancel_request(...)`，这样比单纯依赖后台请求回调更稳定。
- 麦克风输入当前走本地 `PortAudio`。Linux 下如果打不开录音设备，优先检查 `Realtime Voice -> Input device index`、`User Capture` 和 `Wake Word Capture` 的采样率。
- `start_response` / `start_voice_turn` 会自动播放远端返回音频；脚本无需再手动把音频流接到 `Audio.Play(...)`。
- `ResetConversationAsync()` / `reset_conversation()` 会清空远端会话历史，适合切场景或开始新的对话主题时调用。
- `timeout` 事件只表示“在指定等待时间内没有等到一轮可提交的用户语音”。它不是错误，也不会自动清空远端会话，是否重置会话由脚本自行决定。
